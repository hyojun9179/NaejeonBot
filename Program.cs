using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    
    // 내전 모집 및 진행 관리를 위한 메모리 데이터 저장소
    private readonly Dictionary<ulong, List<ulong>> _naejeonParticipants = new Dictionary<ulong, List<ulong>>();
    private readonly object _participantsLock = new object();

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            // 💡 유저를 다른 음성 채널로 이동시키려면 GuildVoiceStates 인텐트가 반드시 필요합니다!
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildVoiceStates
        };
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync; 
        _client.SlashCommandExecuted += SlashCommandHandler; 
        _client.ButtonExecuted += ButtonExecutedAsync; 

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // Render 우회용 가짜 웹서버
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

    // 1. 디스코드 슬래시 명령어들을 등록 (/내전, /내전시작, /게임끝, /게임쫑)
// 1. 디스코드 서버에 대기업 봇처럼 입력창이 뜨는 슬래시 명령어 등록 (기존 명령어 초기화 포함)
    private async Task ReadyAsync()
    {
        try
        {
            // 💡 [핵심 패치] 디스코드 서버에 등록되어 있던 기존 글로벌 명령어들을 전부 삭제하여 초기화합니다.
            // 이 작업을 해주어야 더 이상 쓰지 않는 '/청소' 같은 명령어들이 완전히 사라집니다!
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(new ArraySegment<ApplicationCommandProperties>());
            Console.WriteLine("🧹 기존 글로벌 명령어 목록을 깨끗하게 청소했습니다!");

            // 새로 사용할 명령어 목록 구성
            var naejeonCmd = new SlashCommandBuilder()
                .WithName("내전")
                .WithDescription("내전 모집글을 생성합니다.")
                .AddOption("날짜", ApplicationCommandOptionType.String, "내전 진행 날짜 (예: 월요일)", isRequired: true)
                .AddOption("시간", ApplicationCommandOptionType.String, "내전 시간 (예: 저녁 7시)", isRequired: true)
                .AddOption("몇시간뒤", ApplicationCommandOptionType.Integer, "몇 시간 뒤에 주최자에게 명단 DM을 보낼지 숫자로만 적으세요", isRequired: true)
                .AddOption("내용", ApplicationCommandOptionType.String, "상세 내용을 적으세요 (예: 롤 내전 5vs5)", isRequired: true);

            var startCmd = new SlashCommandBuilder()
                .WithName("내전시작")
                .WithDescription("내전을 시작하고 공수 선택 버튼을 띄웁니다. (대기방 유저 이동용)");

            var endCmd = new SlashCommandBuilder()
                .WithName("게임끝")
                .WithDescription("진행 중인 판을 끝내고 공수방 인원을 대기방으로 한꺼번에 불러옵니다.");

            var finishCmd = new SlashCommandBuilder()
                .WithName("게임쫑")
                .WithDescription("오늘 내전 일정을 완전히 종료합니다.");

            // 새로운 명령어들만 디스코드에 깔끔하게 새로 등록
            await _client.CreateGlobalApplicationCommandAsync(naejeonCmd.Build());
            await _client.CreateGlobalApplicationCommandAsync(startCmd.Build());
            await _client.CreateGlobalApplicationCommandAsync(endCmd.Build());
            await _client.CreateGlobalApplicationCommandAsync(finishCmd.Build());
            
            Console.WriteLine("🤖 새로운 이동 관련 슬래시 명령어 등록 완료!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"명령어 등록 중 오류 발생: {ex.Message}");
        }
    }

    // 2. 명령어 분기 처리
    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var user = command.User as SocketGuildUser;
        if (user == null) return;

        // 권한 체크: '내전운영진' 역할 보유 여부 확인
        string adminRoleName = "내전운영진";
        bool hasRole = user.Roles.Any(r => r.Name == adminRoleName);

        if (!hasRole)
        {
            await command.FollowupAsync("❌ 권한이 없습니다. '내전운영진' 역할만 명령어를 사용할 수 있습니다.", ephemeral: true);
            return;
        }

        switch (command.CommandName)
        {
            case "내전":
                await HandleNaejeon(command);
                break;
            case "내전시작":
                await HandleNaejeonStart(command);
                break;
            case "게임끝":
                await HandleGameEnd(command);
                break;
            case "게임쫑":
                await HandleNaejeonFinish(command);
                break;
        }
    }

    // [/내전] 모집글 생성 처리
    private async Task HandleNaejeon(SocketSlashCommand command)
    {
        var options = command.Data.Options;
        string data_date = options.FirstOrDefault(o => o.Name == "날짜")?.Value.ToString() ?? "";
        string data_time = options.FirstOrDefault(o => o.Name == "시간")?.Value.ToString() ?? "";
        long data_timer = (long)(options.FirstOrDefault(o => o.Name == "몇시간뒤")?.Value ?? 0L);
        string data_content = options.FirstOrDefault(o => o.Name == "내용")?.Value.ToString() ?? "";

        if (data_timer <= 0)
        {
            await command.FollowupAsync("❌ '몇시간뒤' 칸에는 1 이상의 숫자만 입력해 주세요!", ephemeral: true);
            return;
        }

        string descriptionText = $"### 일시: {data_date} {data_time}\n" +
                                 $"### 알림 설정: {data_timer}시간 뒤 주최자 호출\n\n" +
                                 $"# 내용: {data_content}\n\n" +
                                 $"아래 **[참여하기]** 버튼을 눌러 명단에 등록하세요!";

        var embed = new EmbedBuilder()
            .WithTitle("#내전 모집")
            .WithDescription(descriptionText)
            .WithColor(Color.Orange)
            .WithFooter(footer => footer.Text = $"주최자 ID: {command.User.Id}")
            .Build();

        var component = new ComponentBuilder()
            .WithButton("참여하기", "join_naejeon", ButtonStyle.Success)
            .Build();

        var originalResponse = await command.FollowupAsync(embed: embed, components: component);

        lock (_participantsLock)
        {
            _naejeonParticipants[originalResponse.Id] = new List<ulong>();
        }

        _ = Task.Run(async () =>
        {
            int delayMilliseconds = (int)data_timer * 60 * 60 * 1000; // 분 단위를 시간 단위로 교정 (timer * 60 * 60 * 1000)
            await Task.Delay(delayMilliseconds);
            await SendNaejeonManageDM(command.User, originalResponse.Id, command.Channel.Id);
        });
    }

    // [/내전시작] 공수 선택 버튼 구현
    private async Task HandleNaejeonStart(SocketSlashCommand command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("⚔️ 내전 팀 배정 시작!")
            .WithDescription("주최자가 팀 구성을 완료했습니다.\n아래 본인의 진영 버튼을 누르면 해당 **공수 음성 대기방으로 자동 이동**됩니다!\n\n*(주의: 디스코드 음성 채널에 먼저 들어가 있어야 이동됩니다)*")
            .WithColor(Color.Blue)
            .Build();

        var component = new ComponentBuilder()
            .WithButton("🔴 공격팀 이동", "move_attack", ButtonStyle.Danger)
            .WithButton("🔵 수비팀 이동", "move_defense", ButtonStyle.Primary)
            .Build();

        await command.FollowupAsync(embed: embed, components: component);
    }

    // [/게임끝] 공격/수비 음성 채널에 있는 인원들을 다시 대기방으로 강제 이동
    private async Task HandleGameEnd(SocketSlashCommand command)
    {
        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        if (guild == null) return;

        // 서버에서 이름 기반으로 채널들 찾기
        var lobbyChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("대기방"));
        var attackChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("공격"));
        var defenseChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("수비"));

        if (lobbyChannel == null)
        {
            await command.FollowupAsync("❌ 서버에서 '대기방'이라는 단어가 들어간 음성 채널을 찾을 수 없습니다.");
            return;
        }

        int movedCount = 0;

        // 공격방 인원 대기방으로 이동
        if (attackChannel != null)
        {
            foreach (var user in attackChannel.ConnectedUsers)
            {
                await user.ModifyAsync(x => x.Channel = lobbyChannel);
                movedCount++;
            }
        }

        // 수비방 인원 대기방으로 이동
        if (defenseChannel != null)
        {
            foreach (var user in defenseChannel.ConnectedUsers)
            {
                await user.ModifyAsync(x => x.Channel = lobbyChannel);
                movedCount++;
            }
        }

        await command.FollowupAsync($"✅ 게임이 종료되어 공/수 채널의 유저 {movedCount}명을 **[{lobbyChannel.Name}]**으로 일괄 이동시켰습니다!");
    }

    // [/게임쫑] 일정 완전 종료
    private async Task HandleNaejeonFinish(SocketSlashCommand command)
    {
        lock (_participantsLock)
        {
            _naejeonParticipants.Clear();
        }
        await command.FollowupAsync("🏁 오늘 진행된 모든 내전 데이터가 초기화되었습니다. 모두 수고하셨습니다!");
    }

    // 3. 버튼 클릭 처리 (참여 신청 및 공격/수비 방 자동 이동)
    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        var user = component.User as SocketGuildUser;
        if (user == null) return;

        // [참여하기] 버튼 처리
        if (component.Data.CustomId == "join_naejeon")
        {
            var msgId = component.Message.Id;
            bool listExists;
            bool alreadyJoined = false;
            int waitingNumber = 0;

            lock (_participantsLock)
            {
                listExists = _naejeonParticipants.ContainsKey(msgId);
                if (listExists)
                {
                    var list = _naejeonParticipants[msgId];
                    if (list.Contains(user.Id)) alreadyJoined = true;
                    else
                    {
                        list.Add(user.Id);
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

            await component.RespondAsync("✅ 내전 참여 등록 완료! DM으로 대기번호가 발송되었습니다.", ephemeral: true);

            try
            {
                string dmMessage = $"# 🎫 내전 참가 확정!\n" +
                                   $"안녕하세요! 신청하신 내전의 **대기번호는 {waitingNumber}번**입니다.\n" +
                                   $"시작 시간에 맞춰 완료 대기해 주세요! 😉";
                await user.SendMessageAsync(dmMessage);
            }
            catch
            {
                Console.WriteLine($"{user.Username}님이 DM을 차단해 대기번호를 발송하지 못했습니다.");
            }
        }
        // [공격팀 이동] 또는 [수비팀 이동] 버튼 처리
        else if (component.Data.CustomId == "move_attack" || component.Data.CustomId == "move_defense")
        {
            // 유저가 음성 채널에 들어가 있는지 체크
            if (user.VoiceChannel == null)
            {
                await component.RespondAsync("❌ 음성 채널(대기방)에 먼저 접속한 뒤 버튼을 눌러주세요!", ephemeral: true);
                return;
            }

            string targetKeyword = component.Data.CustomId == "move_attack" ? "공격" : "수비";
            var targetChannel = user.Guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains(targetKeyword));

            if (targetChannel == null)
            {
                await component.RespondAsync($"❌ 서버 내 이름에 '{targetKeyword}'이(가) 포함된 음성 채널을 찾을 수 없습니다.", ephemeral: true);
                return;
            }

            try
            {
                // 유저를 공격/수비 채널로 자동 드래그(이동)
                await user.ModifyAsync(x => x.Channel = targetChannel);
                await component.RespondAsync($"🏃 **[{targetChannel.Name}]** 채널로 이동되었습니다!", ephemeral: true);
            }
            catch (Exception ex)
            {
                await component.RespondAsync("❌ 채널 이동 도중 오류가 발생했습니다. 권한 설정을 확인하세요.", ephemeral: true);
                Console.WriteLine($"이동 오류: {ex.Message}");
            }
        }
        // 원격 호출 버튼 처리
        else if (component.Data.CustomId.StartsWith("mention_"))
        {
            var parts = component.Data.CustomId.Split('_');
            ulong targetUserId = ulong.Parse(parts[1]);
            ulong channelId = ulong.Parse(parts[2]);

            var channel = _client.GetChannel(channelId) as ISocketMessageChannel;
            if (channel != null)
            {
                await channel.SendMessageAsync($"내전이 시작합니다 빨리 와주세요! <@{targetUserId}>");
                await component.RespondAsync("🔔 해당 유저를 성공적으로 호출했습니다.", ephemeral: true);
            }
        }
    }

    // 주최자 원격 관리 DM 전송
    private async Task SendNaejeonManageDM(IUser hostUser, ulong messageId, ulong channelId)
    {
        List<ulong> participants = null;

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