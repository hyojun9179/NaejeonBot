using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    // 내전 모집글별로 참가 신청한 유저들의 ID를 순서대로 저장하는 방 (메시지ID, 유저ID 리스트)
    private readonly Dictionary<ulong, List<ulong>> _naejeonParticipants = new Dictionary<ulong, List<ulong>>();
    // 💡 여러 명이 거의 동시에 버튼을 눌러도 명단이 꼬이지 않도록 보호하는 잠금 장치
    private readonly object _participantsLock = new object();

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            // 봇이 서버의 유저 정보(이름, DM 전송 등)를 정상적으로 가져오기 위한 인텐트 설정
            GatewayIntents = GatewayIntents.AllUnprivileged
        };
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync; // 봇이 켜지면 명령어를 디스코드에 등록
        _client.SlashCommandExecuted += SlashCommandHandler; // 슬래시 명령어 입력 처리
        _client.ButtonExecuted += ButtonExecutedAsync; // 버튼 클릭 처리

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // 💡 Render의 포트 감시를 속이기 위한 무료 전용 가짜 웹서버 코드!
        _ = Task.Run(() =>
        {
            try
            {
                string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
                var listener = new System.Net.HttpListener();
                listener.Prefixes.Add($"http://*:{port}/");
                listener.Start();
                Console.WriteLine($"🌐 가짜 웹 서버가 {port} 포트에서 작동 중입니다. (무료 우회용)");
                while (true)
                {
                    var context = listener.GetContext();
                    context.Response.StatusCode = 200;
                    using (var writer = new System.IO.StreamWriter(context.Response.OutputStream))
                    {
                        writer.Write("Bot is Running!");
                    }
                    context.Response.Close();
                }
            }
            catch (Exception ex) { Console.WriteLine($"웹 서버 에러: {ex.Message}"); }
        });

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log) { Console.WriteLine(log.ToString()); return Task.CompletedTask; }

    // 1. 디스코드 서버에 대기업 봇처럼 입력창이 뜨는 '/내전' 슬래시 명령어 등록
    private async Task ReadyAsync()
    {
        var guildCommand = new SlashCommandBuilder()
            .WithName("내전")
            .WithDescription("내전 모집글을 생성합니다.")
            .AddOption("날짜", ApplicationCommandOptionType.String, "내전 진행 날짜를 입력해라ㅇㅇ (예: 월요일)", isRequired: true)
            .AddOption("시간", ApplicationCommandOptionType.String, "내전 시간을 입력하셈; (예: 저녁 7시)", isRequired: true)
            .AddOption("몇시간뒤", ApplicationCommandOptionType.Integer, "몇 시간 뒤에 주최자에게 명단 DM을 보낼지 숫자로만 적으세요 (예: 3)", isRequired: true)
            .AddOption("내용", ApplicationCommandOptionType.String, "게임 종류나 상세 내용을 적으세요 (예: 롤 내전 5vs5)", isRequired: true);

        try
        {
            await _client.CreateGlobalApplicationCommandAsync(guildCommand.Build());
            Console.WriteLine("🤖 슬래시 명령어 등록 완료! 디스코드 창에서 /내전 을 쳐보세요.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"명령어 등록 중 오류 발생: {ex.Message}");
        }
    }

    // 2. 사용자가 '/내전' 명령어를 치고 값을 전송했을 때 처리
    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        if (command.CommandName != "내전") return;

        // 💡 [핵심 패치] '애플리케이션이 응답하지 않았어요' 에러를 막기 위해 봇을 '생각 중...' 상태로 전환 (3초 제한 해제)
        await command.DeferAsync();

        var user = command.User as SocketGuildUser;
        if (user == null) return;

        // 권한 체크: '내전운영진' 역할이 있는 사람만 주최 가능
        string adminRoleName = "내전운영진";
        bool hasRole = false;
        foreach (var role in user.Roles)
        {
            if (role.Name == adminRoleName) { hasRole = true; break; }
        }

        if (!hasRole)
        {
            await command.FollowupAsync("❌ 권한이 없습니다. '내전운영진' 역할만 내전을 열 수 있습니다.", ephemeral: true);
            return;
        }

        // 사용자가 입력창에 채워 넣은 옵션 값들을 안전하게 하나씩 뽑아오기 (오류 유발 코드 완전 제거)
        var options = command.Data.Options;
        string data_date = "";
        string data_time = "";
        long data_timer = 0;
        string data_content = "";

        foreach (var opt in options)
        {
            if (opt.Name == "날짜") data_date = opt.Value.ToString();
            if (opt.Name == "시간") data_time = opt.Value.ToString();
            if (opt.Name == "몇시간뒤") data_timer = (long)opt.Value;
            if (opt.Name == "내용") data_content = opt.Value.ToString();
        }

        if (data_timer <= 0)
        {
            await command.FollowupAsync("❌ '몇시간뒤' 칸에는 1 이상의 숫자만 입력해 주세요!", ephemeral: true);
            return;
        }

        // 임베드 메세지 디자인 생성
        string boldTitle = "#내전 모집";
        string descriptionText = $"### 일시: {data_date} {data_time}\n" +
                                 $"### 알림 설정: {data_timer}시간 뒤 주최자 호출\n\n" +
                                 $"# 내용: {data_content}\n\n" +
                                 $"아래 **[참여하기]** 버튼을 눌러 명단에 등록하세요!";

        var embed = new EmbedBuilder()
            .WithTitle(boldTitle)
            .WithDescription(descriptionText)
            .WithColor(Color.Orange)
            .WithFooter(footer => footer.Text = $"주최자 ID: {command.User.Id}")
            .Build();

        // 초록색 [참여하기] 버튼 부착
        var component = new ComponentBuilder()
            .WithButton("참여하기", "join_naejeon", ButtonStyle.Success)
            .Build();

        // 최종 모집글을 채팅방에 전송
        var originalResponse = await command.FollowupAsync(embed: embed, components: component);

        // 이 모집글 전용 참가자 명단 리스트 초기화
        // 💡 딕셔너리에 새 키를 추가하는 부분도 lock으로 보호 (다른 /내전 명령이 동시에 실행될 수 있으므로)
        lock (_participantsLock)
        {
            _naejeonParticipants[originalResponse.Id] = new List<ulong>();
        }

        // 비동기 타이머 작동 (설정한 시간 뒤에 주최자에게 DM 발송)
        _ = Task.Run(async () =>
        {
            int delayMilliseconds = (int)data_timer * 60 * 1000; // 시간 단위를 밀리초로 변환
            await Task.Delay(delayMilliseconds);
            await SendNaejeonManageDM(command.User, originalResponse.Id, command.Channel.Id);
        });
    }

    // 3. 버튼 클릭 처리 (참가 신청 및 대기번호 발송)
    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        // 유저가 [참여하기] 버튼을 눌렀을 때
        if (component.Data.CustomId == "join_naejeon")
        {
            var msgId = component.Message.Id;

            bool listExists;
            bool alreadyJoined = false;
            int waitingNumber = 0;

            // 💡 [핵심 수정] "존재 확인 + 중복 체크 + 추가 + 번호 계산"을 하나의 lock 안에서
            //     한 번에 처리해서, 여러 명이 동시에 눌러도 순서대로 안전하게 처리되도록 함.
            //     (await는 lock 안에서 쓸 수 없으므로 여기서는 동기 작업만 수행)
            lock (_participantsLock)
            {
                listExists = _naejeonParticipants.ContainsKey(msgId);
                if (listExists)
                {
                    var list = _naejeonParticipants[msgId];
                    if (list.Contains(component.User.Id))
                    {
                        alreadyJoined = true;
                    }
                    else
                    {
                        list.Add(component.User.Id);
                        // 💡 리스트에 들어간 유저의 순서가 바로 대기 번호가 됨
                        waitingNumber = list.Count;
                    }
                }
            }

            if (!listExists) return;

            if (alreadyJoined)
            {
                await component.RespondAsync("이미 참여 등록이 되어 있습니다.", ephemeral: true);
                return;
            }

            // 화면에는 다른 사람에게 안 보이고 본인에게만 성공 메시지 표시
            await component.RespondAsync("✅ 내전 참여 등록 완료! DM으로 대기번호가 발송되었습니다.", ephemeral: true);

            // 신청한 유저에게 즉시 대기 번호 DM 꽂아주기
            try
            {
                string dmMessage = $"# 🎫 내전 참가 확정!\n" +
                                   $"안녕하세요! 신청하신 내전의 **대기번호는 {waitingNumber}번**입니다.\n" +
                                   $"시작 시간에 맞춰 완료 대기해 주세요! 😉";
                await component.User.SendMessageAsync(dmMessage);
            }
            catch (Exception)
            {
                // 유저가 디스코드 설정에서 '서버 멤버가 보내는 DM 허용'을 꺼둔 경우 에러 방지
                Console.WriteLine($"{component.User.Username}님이 DM을 차단해 두어 대기번호를 발송하지 못했습니다.");
            }
        }
        // 주최자가 자신의 DM창에서 특정 유저 [호출하기] 버튼을 눌렀을 때
        else if (component.Data.CustomId.StartsWith("mention_"))
        {
            var parts = component.Data.CustomId.Split('_');
            ulong targetUserId = ulong.Parse(parts[1]);
            ulong channelId = ulong.Parse(parts[2]);

            var channel = _client.GetChannel(channelId) as ISocketMessageChannel;
            if (channel != null)
            {
                // 진짜 공용 채팅방에 멘션과 함께 출석 호출 메시지 발송
                await channel.SendMessageAsync($"내전이 시작합니다 빨리 와주세요! <@{targetUserId}>");
                await component.RespondAsync("🔔 해당 유저를 성공적으로 호출했습니다.", ephemeral: true);
            }
        }
    }

    // 4. 타이머가 종료되면 주최자에게 참가자 명단을 유저별 버튼 형태로 DM 전송
    private async Task SendNaejeonManageDM(IUser hostUser, ulong messageId, ulong channelId)
    {
        List<ulong> participants = null;

        // 💡 리스트를 읽는 시점에도 다른 유저가 동시에 참여 신청 중일 수 있으므로
        //     lock으로 감싸고, 이후 순회(foreach)는 복사본으로 안전하게 처리
        lock (_participantsLock)
        {
            if (_naejeonParticipants.ContainsKey(messageId) && _naejeonParticipants[messageId].Count > 0)
            {
                participants = new List<ulong>(_naejeonParticipants[messageId]);
            }
        }

        if (participants == null)
        {
            await hostUser.SendMessageAsync("# 😢 약속된 시간이 되었지만 참가 신청자가 아무도 없습니다.");
            return;
        }

        var dmComponent = new ComponentBuilder();

        await hostUser.SendMessageAsync("# ⏰ 약속된 내전 시간이 되었습니다!\n채팅방으로 원격 호출할 유저의 버튼을 누르세요.");

        // 신청한 유저들의 이름을 따서 각각 버튼으로 만들어 주최자 DM에 배치
        foreach (var userId in participants)
        {
            var user = _client.GetUser(userId);
            if (user != null)
            {
                dmComponent.WithButton($"{user.Username} 호출하기", $"mention_{userId}_{channelId}", ButtonStyle.Primary);
            }
        }

        await hostUser.SendMessageAsync("📋 **참가자 원격 호출 목록:**", components: dmComponent.Build());
    }
}