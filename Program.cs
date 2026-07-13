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

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            // 💡 [필수 패치] 메시지 청소(삭제) 기능을 정상적으로 수행하기 위해 MessageContent 인텐트 추가
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
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
                listener.Prefixes.Add($"http://+:{port}/");
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

    // 1. 슬래시 명령어 등록
    private async Task ReadyAsync()
    {
        // 내전 명령어
        var guildCommand = new SlashCommandBuilder()
            .WithName("내전")
            .WithDescription("내전 모집글을 생성합니다.")
            .AddOption("날짜", ApplicationCommandOptionType.String, "내전 진행 날짜를 입력해라ㅇㅇ (예: 월요일)", isRequired: true)
            .AddOption("시간", ApplicationCommandOptionType.String, "내전 시간을 입력하셈; (예: 저녁 7시)", isRequired: true)
            .AddOption("몇시간뒤", ApplicationCommandOptionType.Integer, "몇 시간 뒤에 주최자에게 명단 DM을 보낼지 숫자로만 적으세요 (예: 3)", isRequired: true)
            .AddOption("내용", ApplicationCommandOptionType.String, "게임 종류나 상세 내용을 적으세요 (예: 롤 내전 5vs5)", isRequired: true);

        // 💡 [신규] 청소 명령어 등록
        var deleteCommand = new SlashCommandBuilder()
            .WithName("청소")
            .WithDescription("채팅방의 메시지를 대량으로 삭제합니다.")
            .AddOption("개수", ApplicationCommandOptionType.Integer, "삭제할 메시지의 개수를 적으세요 (1~100)", isRequired: true);

        try
        {
            await _client.CreateGlobalApplicationCommandAsync(guildCommand.Build());
            await _client.CreateGlobalApplicationCommandAsync(deleteCommand.Build()); // 청소 추가
            Console.WriteLine("🤖 슬래시 명령어 등록 완료! 디스코드 창에서 /내전 또는 /청소 를 쳐보세요.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"명령어 등록 중 오류 발생: {ex.Message}");
        }
    }

    // 2. 사용자가 슬래시 명령어를 쳤을 때 처리
    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        // ---------------- [기존 내전 명령어 처리] ----------------
        if (command.CommandName != "내전") return;

        await command.DeferAsync();

        var guildUser = command.User as SocketGuildUser;
        if (guildUser == null) return;

        string adminRoleName = "내전운영진";
        bool hasRole = false;
        foreach (var role in guildUser.Roles)
        {
            if (role.Name == adminRoleName) { hasRole = true; break; }
        }

        if (!hasRole)
        {
            await command.FollowupAsync("❌ 권한이 없습니다. '내전운영진' 역할만 내전을 열 수 있습니다.", ephemeral: true);
            return;
        }

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

        string boldTitle = "#내전 모집";
        string descriptionText = $"### 일시: {data_date} {data_time}\n" +
                                 $"### 알림 설정: {data_timer}시간 뒤 전체 DM 호출\n\n" + // 문구 수정
                                 $"# 내용: {data_content}\n\n" +
                                 $"아래 **[참여하기]** 버튼을 눌러 명단에 등록하세요!";

        var embed = new EmbedBuilder()
            .WithTitle(boldTitle)
            .WithDescription(descriptionText)
            .WithColor(Color.Orange)
            .WithFooter(footer => footer.Text = $"주최자 ID: {command.User.Id}")
            .Build();

        var component = new ComponentBuilder()
            .WithButton("참여하기", "join_naejeon", ButtonStyle.Success)
            .Build();

        var originalResponse = await command.FollowupAsync(embed: embed, components: component);
        
        _naejeonParticipants[originalResponse.Id] = new List<ulong>();

        // 비동기 타이머 작동 (시간 종료 후 주최자 DM 발송 + 모든 유저 자동 DM 발송)
        _ = Task.Run(async () =>
        {
            int delayMilliseconds = (int)data_timer * 60 * 60 * 1000; // 💡 분 단위 버그 수정: 시간 단위로 정상 작동하게 조절
            await Task.Delay(delayMilliseconds);
            
            // 1. 주최자에게 관리자 명단 DM 발송
            await SendNaejeonManageDM(command.User, originalResponse.Id, command.Channel.Id);
            
            // 💡 [신규] 2. 신청한 모든 유저들에게 내전 시작 알림 DM 일괄 자동 전송
            await SendNotificationToAllParticipants(originalResponse.Id, data_date, data_time, data_content);
        });
    }

    // 3. 버튼 클릭 처리
    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId == "join_naejeon")
        {
            var msgId = component.Message.Id;
            if (_naejeonParticipants.ContainsKey(msgId))
            {
                if (!_naejeonParticipants[msgId].Contains(component.User.Id))
                {
                    _naejeonParticipants[msgId].Add(component.User.Id);
                    int waitingNumber = _naejeonParticipants[msgId].Count;

                    await component.RespondAsync("✅ 내전 참여 등록 완료! DM으로 대기번호가 발송되었습니다.", ephemeral: true);

                    try
                    {
                        string dmMessage = $"# 🎫 내전 참가 확정!\n" +
                                           $"안녕하세요! 신청하신 내전의 **대기번호는 {waitingNumber}번**입니다.\n" +
                                           $"시작 시간에 맞춰 완료 대기해 주세요! 😉";
                        await component.User.SendMessageAsync(dmMessage);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"{component.User.Username}님이 DM을 차단해 두어 대기번호를 발송하지 못했습니다.");
                    }
                }
                else
                {
                    await component.RespondAsync("이미 참여 등록이 되어 있습니다.", ephemeral: true);
                }
            }
        }
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

    // 4. 주최자 관리용 DM 발송 기기
    private async Task SendNaejeonManageDM(IUser hostUser, ulong messageId, ulong channelId)
    {
        if (!_naejeonParticipants.ContainsKey(messageId) || _naejeonParticipants[messageId].Count == 0)
        {
            await hostUser.SendMessageAsync("# 😢 약속된 시간이 되었지만 참가 신청자가 아무도 없습니다.");
            return;
        }

        var dmComponent = new ComponentBuilder();
        var participants = _naejeonParticipants[messageId];

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

    // 💡 [신규 기능] 5. 시간이 되면 등록한 모든 유저에게 자동으로 호출 DM을 날려주는 기기
    private async Task SendNotificationToAllParticipants(ulong messageId, string date, string time, string content)
    {
        if (!_naejeonParticipants.ContainsKey(messageId)) return;

        var participants = _naejeonParticipants[messageId];
        string alertMessage = $"# 🚨 5분 전 알림!\n" +
                              $"신청하신 내전 시간이 임박했습니다. 디스코드 채널로 접속해 주세요!\n" +
                              $"* **일시:** {date} {time}\n" +
                              $"* **내용:** {content}\n" +
                              $"지각하면 명단에서 제외될 수 있으니 대기 부탁드립니다! 🏃‍♂️💨";

        foreach (var userId in participants)
        {
            try
            {
                var user = _client.GetUser(userId);
                if (user != null)
                {
                    await user.SendMessageAsync(alertMessage);
                }
            }
            catch (Exception)
            {
                // 특정 유저가 DM 차단 상태일 때 전체 시스템이 안 멈추도록 예외 처리
                Console.WriteLine($"자동 알림 실패: 유저 ID {userId}번이 DM을 닫아두었습니다.");
            }
        }
    }
}