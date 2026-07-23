using System;
using System.Collections.Generic;
using System.Globalization;
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

    // ⚔️ 교체혈전 데이터 저장소 및 잠금 객체
    private readonly object _ladderLock = new object();
    private readonly List<string> _ladderRanks = new List<string>
    {
        "준", "재동", "고래", "김치", "지원", "리스", "대웅", "하루비", 
        "승현", "혁", "윤", "우망", "제이크", "쿠쿠", "투스", "예은", 
        "네보", "초원", "레카", "영식", "우노", "효준", "우지"
    };

    // 💡 초기 멤버는 이미 '첫 교체혈전 찬스'를 사용한 것으로 설정 (신규 멤버만 찬스 부여)
    private readonly HashSet<string> _firstTimerUsed = new HashSet<string>
    {
        "준", "재동", "고래", "김치", "지원", "리스", "대웅", "하루비", 
        "승현", "혁", "윤", "우망", "제이크", "쿠쿠", "투스", "예은", 
        "네보", "초원", "레카", "영식", "우노", "효준", "우지"
    };

    // 주차별(주간) 대결 기록: Key = "년도-주차_도전자_피도전자"
    private readonly HashSet<string> _weeklyMatchHistory = new HashSet<string>();

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
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

    // 1. 디스코드 슬래시 명령어 일괄 등록
    private async Task ReadyAsync()
    {
        try
        {
            var commandList = new List<ApplicationCommandProperties>();

            // [기존 내전 명령어]
            commandList.Add(new SlashCommandBuilder()
                .WithName("내전")
                .WithDescription("내전 모집글을 생성합니다.")
                .AddOption("날짜", ApplicationCommandOptionType.String, "내전 진행 날짜 (예: 월요일)", isRequired: true)
                .AddOption("시간", ApplicationCommandOptionType.String, "내전 시간 (예: 저녁 7시)", isRequired: true)
                .AddOption("몇시간뒤", ApplicationCommandOptionType.Integer, "몇 시간 뒤에 주최자에게 명단 DM을 보낼지 숫자로만 적으세요", isRequired: true)
                .AddOption("내용", ApplicationCommandOptionType.String, "상세 내용을 적으세요 (예: 롤 내전 5vs5)", isRequired: true)
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("내전시작")
                .WithDescription("내전을 시작하고 공수 선택 버튼을 띄웁니다. (대기방 유저 이동용)")
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("게임끝")
                .WithDescription("진행 중인 판을 끝내고 공수방 인원을 대기방으로 한꺼번에 불러옵니다.")
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("게임쫑")
                .WithDescription("오늘 내전 일정을 완전히 종료합니다.")
                .Build());

            // [💤 봇 잠수방 상주 명령어]
            commandList.Add(new SlashCommandBuilder()
                .WithName("잠수")
                .WithDescription("봇이 잠수방에 들어와 마이크와 헤드셋을 끄고 자리를 지킵니다.")
                .Build());

            // [⚔️ 교체혈전 명령어]
            commandList.Add(new SlashCommandBuilder()
                .WithName("교체순위")
                .WithDescription("현재 교체혈전 순위 리스트를 확인합니다.")
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("교체신청")
                .WithDescription("상대방에게 교체혈전을 신청합니다.")
                .AddOption("본인이름", ApplicationCommandOptionType.String, "본인의 이름을 입력하세요", isRequired: true)
                .AddOption("상대이름", ApplicationCommandOptionType.String, "지목할 상대방의 이름을 입력하세요", isRequired: true)
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("교체결과")
                .WithDescription("교체혈전 결과를 입력하여 순위를 갱신합니다. (운영진 전용)")
                .AddOption("승리자", ApplicationCommandOptionType.String, "승리한 사람 이름", isRequired: true)
                .AddOption("패배자", ApplicationCommandOptionType.String, "패배한 사람 이름", isRequired: true)
                .AddOption("첫교체패배여부", ApplicationCommandOptionType.Boolean, "신규 유저가 '첫 교체혈전'에서 패배했나요?", isRequired: false)
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("교체멤버추가")
                .WithDescription("교체혈전 리스트에 새로운 멤버를 추가합니다. (운영진 전용)")
                .AddOption("이름", ApplicationCommandOptionType.String, "추가할 멤버 이름", isRequired: true)
                .Build());

            commandList.Add(new SlashCommandBuilder()
                .WithName("교체멤버삭제")
                .WithDescription("교체혈전 리스트에서 멤버를 삭제합니다. (운영진 전용)")
                .AddOption("이름", ApplicationCommandOptionType.String, "삭제할 멤버 이름", isRequired: true)
                .Build());

            // 모든 슬래시 명령어 일괄 등록
            await _client.BulkOverwriteGlobalApplicationCommandsAsync(commandList.ToArray());
            Console.WriteLine("🤖 총 10개의 모든 슬래시 명령어 등록 완료!");
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

        string adminRoleName = "내전운영진";
        bool hasAdminRole = user.Roles.Any(r => r.Name == adminRoleName);

        switch (command.CommandName)
        {
            case "내전":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleNaejeon(command);
                break;
            case "내전시작":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleNaejeonStart(command);
                break;
            case "게임끝":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleGameEnd(command);
                break;
            case "게임쫑":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleNaejeonFinish(command);
                break;
            case "잠수":
                await HandleAfk(command);
                break;

            // 교체혈전 핸들러
            case "교체순위":
                await HandleRankList(command);
                break;
            case "교체신청":
                await HandleChallenge(command);
                break;
            case "교체결과":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleChallengeResult(command);
                break;
            case "교체멤버추가":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleAddMember(command);
                break;
            case "교체멤버삭제":
                if (!CheckAdmin(hasAdminRole, command)) return;
                await HandleRemoveMember(command);
                break;
        }
    }

    private bool CheckAdmin(bool hasAdminRole, SocketSlashCommand command)
    {
        if (!hasAdminRole)
        {
            command.FollowupAsync("❌ 권한이 없습니다. '내전운영진' 역할만 사용할 수 있습니다.", ephemeral: true);
            return false;
        }
        return true;
    }

    // ==========================================
    // 💤 봇 잠수방 상주 기능
    // ==========================================
    private async Task HandleAfk(SocketSlashCommand command)
    {
        var user = command.User as SocketGuildUser;
        if (user == null) return;

        // 서버에서 '잠수'라는 이름이 포함된 음성 채널 탐색 (없으면 명령어를 실행한 유저가 접속 중인 음성 채널)
        var afkChannel = user.Guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("잠수")) ?? user.VoiceChannel;

        if (afkChannel == null)
        {
            await command.FollowupAsync("❌ 서버 내에서 '잠수' 음성 채널을 찾을 수 없거나, 음성 채널에 들어가 있지 않으십니다.", ephemeral: true);
            return;
        }

        try
        {
            // 🤖 봇 자신이 해당 음성 채널에 직접 접속!
            // selfMute: true (마이크 끔), selfDeaf: true (헤드셋 끔)
            await afkChannel.ConnectAsync(selfDeaf: true, selfMute: true);

            await command.FollowupAsync($"💤 **봇이 [{afkChannel.Name}] 채널에 접속했습니다!** 마이크와 헤드셋을 끄고 자리를 유지합니다.");
        }
        catch (Exception ex)
        {
            await command.FollowupAsync("❌ 봇이 음성 채널에 접속하는 도중 오류가 발생했습니다.", ephemeral: true);
            Console.WriteLine($"잠수 접속 오류: {ex.Message}");
        }
    }

    // ==========================================
    // ⚔️ 교체혈전 로직
    // ==========================================

    private async Task HandleRankList(SocketSlashCommand command)
    {
        lock (_ladderLock)
        {
            string rankText = "";
            for (int i = 0; i < _ladderRanks.Count; i++)
            {
                string firstTimerBadge = !_firstTimerUsed.Contains(_ladderRanks[i]) ? " 🔰(신규 첫혈전 찬스)" : "";
                rankText += $"**{i + 1}위**: {_ladderRanks[i]}{firstTimerBadge}\n";
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏆 교체혈전 현재 순위표")
                .WithDescription(rankText)
                .WithColor(Color.Gold)
                .WithFooter(footer => footer.Text = "월요일마다 동일인물 재신청 제한이 초기화됩니다.")
                .Build();

            command.FollowupAsync(embed: embed);
        }
    }

    private async Task HandleChallenge(SocketSlashCommand command)
    {
        var options = command.Data.Options;
        string challenger = options.FirstOrDefault(o => o.Name == "본인이름")?.Value.ToString().Trim() ?? "";
        string defender = options.FirstOrDefault(o => o.Name == "상대이름")?.Value.ToString().Trim() ?? "";

        lock (_ladderLock)
        {
            int challengerIdx = _ladderRanks.IndexOf(challenger);
            int defenderIdx = _ladderRanks.IndexOf(defender);

            if (challengerIdx == -1)
            {
                command.FollowupAsync($"❌ '{challenger}'님은 교체혈전 명단에 없습니다.", ephemeral: true);
                return;
            }
            if (defenderIdx == -1)
            {
                command.FollowupAsync($"❌ '{defender}'님은 교체혈전 명단에 없습니다.", ephemeral: true);
                return;
            }
            if (challengerIdx == defenderIdx)
            {
                command.FollowupAsync("❌ 자기 자신에게는 신청할 수 없습니다.", ephemeral: true);
                return;
            }

            string currentWeekKey = GetCurrentWeekKey();
            string matchKey = $"{currentWeekKey}_{challenger}_{defender}";

            if (_weeklyMatchHistory.Contains(matchKey))
            {
                command.FollowupAsync($"❌ 이번 주에 이미 **{defender}**님에게 교체혈전을 신청하셨습니다! (월요일마다 리셋)", ephemeral: true);
                return;
            }

            bool isFirstTimer = !_firstTimerUsed.Contains(challenger);

            if (!isFirstTimer)
            {
                int diff = challengerIdx - defenderIdx;
                if (diff <= 0)
                {
                    command.FollowupAsync("❌ 자신보다 상위 순위인 사람에게만 신청할 수 있습니다.", ephemeral: true);
                    return;
                }
                if (diff > 5)
                {
                    command.FollowupAsync($"❌ 본인보다 위로 최대 5단계까지만 신청 가능합니다. (현재 차이: {diff}단계)", ephemeral: true);
                    return;
                }
            }

            _weeklyMatchHistory.Add(matchKey);

            string firstTimeNotice = isFirstTimer ? "🔰 **[신규 멤버 첫 교체혈전 찬스 사용!]** 순위 제한 없이 자유 지목되었습니다! (패배 시 맨 뒷순위 이동)" : "";

            var embed = new EmbedBuilder()
                .WithTitle("⚔️ 교체혈전 신청 완료!")
                .WithDescription($"**{challenger}** (순위: {challengerIdx + 1}위) 🆚 **{defender}** (순위: {defenderIdx + 1}위)\n\n" +
                                 $"{firstTimeNotice}\n\n" +
                                 $"--- **[📜 경기 규칙 필독]** ---\n" +
                                 $"1. 👑 **무조건 방장 앞에서 진행해야 합니다.**\n" +
                                 $"2. 🎯 **게임 모드:** 무조건 **난투 A**\n" +
                                 $"3. 🔫 **사용 가능 총기:** 벤달, 팬텀, 가디언, 셰리프, 고스트, 클래식\n" +
                                 $"4. ⚠️ **승낙 거부 시:** 정당한 사유 없이 거부할 경우 패배 처리됩니다.\n" +
                                 $"5. 📅 **동일 대상 재신청:** 다음 주 월요일 이후 가능합니다.")
                .WithColor(Color.Red)
                .Build();

            command.FollowupAsync(embed: embed);
        }
    }

    private async Task HandleChallengeResult(SocketSlashCommand command)
    {
        var options = command.Data.Options;
        string winner = options.FirstOrDefault(o => o.Name == "승리자")?.Value.ToString().Trim() ?? "";
        string loser = options.FirstOrDefault(o => o.Name == "패배자")?.Value.ToString().Trim() ?? "";
        bool isFirstTimerLoss = (bool)(options.FirstOrDefault(o => o.Name == "첫교체패배여부")?.Value ?? false);

        lock (_ladderLock)
        {
            int winnerIdx = _ladderRanks.IndexOf(winner);
            int loserIdx = _ladderRanks.IndexOf(loser);

            if (winnerIdx == -1 || loserIdx == -1)
            {
                command.FollowupAsync("❌ 입력한 이름이 교체혈전 명단에 없습니다.", ephemeral: true);
                return;
            }

            _firstTimerUsed.Add(winner);
            _firstTimerUsed.Add(loser);

            string resultMsg = "";

            if (isFirstTimerLoss)
            {
                _ladderRanks.Remove(loser);
                _ladderRanks.Add(loser);
                resultMsg = $"💥 **{loser}**님이 첫 교체혈전에서 패배하여 **맨 뒷순위({_ladderRanks.Count}위)**로 이동되었습니다!";
            }
            else if (winnerIdx > loserIdx)
            {
                _ladderRanks.Remove(winner);
                _ladderRanks.Insert(loserIdx, winner);
                resultMsg = $"🎉 **{winner}**님이 **{loser}**님을 꺾고 **{loserIdx + 1}위**로 상승했습니다!";
            }
            else
            {
                resultMsg = $"🛡️ **{winner}**님이 방어에 성공하여 기존 순위({winnerIdx + 1}위)를 유지했습니다.";
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏆 교체혈전 결과 발표")
                .WithDescription(resultMsg)
                .WithColor(Color.Green)
                .Build();

            command.FollowupAsync(embed: embed);
        }
    }

    private async Task HandleAddMember(SocketSlashCommand command)
    {
        string newName = command.Data.Options.FirstOrDefault(o => o.Name == "이름")?.Value.ToString().Trim() ?? "";

        lock (_ladderLock)
        {
            if (_ladderRanks.Contains(newName))
            {
                command.FollowupAsync("❌ 이미 명단에 존재하는 이름입니다.", ephemeral: true);
                return;
            }

            _ladderRanks.Add(newName);
            _firstTimerUsed.Remove(newName);

            command.FollowupAsync($"✅ **{newName}**님이 교체혈전 명단 맨 뒷순위({_ladderRanks.Count}위)에 추가되었습니다! 🔰 (신규 찬스 부여됨)");
        }
    }

    private async Task HandleRemoveMember(SocketSlashCommand command)
    {
        string removeName = command.Data.Options.FirstOrDefault(o => o.Name == "이름")?.Value.ToString().Trim() ?? "";

        lock (_ladderLock)
        {
            if (!_ladderRanks.Contains(removeName))
            {
                command.FollowupAsync("❌ 명단에 존재하지 않는 이름입니다.", ephemeral: true);
                return;
            }

            _ladderRanks.Remove(removeName);
            _firstTimerUsed.Remove(removeName);
            command.FollowupAsync($"🗑️ **{removeName}**님이 교체혈전 명단에서 삭제되었습니다.");
        }
    }

    private string GetCurrentWeekKey()
    {
        DateTime now = DateTime.UtcNow.AddHours(9);
        DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(now);
        if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
        {
            now = now.AddDays(3);
        }
        int weekNum = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{now.Year}-W{weekNum}";
    }

    // ==========================================
    // [기존] 내전 기능
    // ==========================================

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
            int delayMilliseconds = (int)data_timer * 60 * 60 * 1000;
            await Task.Delay(delayMilliseconds);
            await SendNaejeonManageDM(command.User, originalResponse.Id, command.Channel.Id);
        });
    }

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

    private async Task HandleGameEnd(SocketSlashCommand command)
    {
        var guild = (command.Channel as SocketGuildChannel)?.Guild;
        if (guild == null) return;

        var lobbyChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("대기방"));
        var attackChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("공격"));
        var defenseChannel = guild.VoiceChannels.FirstOrDefault(c => c.Name.Contains("수비"));

        if (lobbyChannel == null)
        {
            await command.FollowupAsync("❌ 서버에서 '대기방'이라는 단어가 들어간 음성 채널을 찾을 수 없습니다.");
            return;
        }

        int movedCount = 0;

        if (attackChannel != null)
        {
            foreach (var user in attackChannel.ConnectedUsers)
            {
                await user.ModifyAsync(x => x.Channel = lobbyChannel);
                movedCount++;
            }
        }

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

    private async Task HandleNaejeonFinish(SocketSlashCommand command)
    {
        lock (_participantsLock)
        {
            _naejeonParticipants.Clear();
        }
        await command.FollowupAsync("🏁 오늘 진행된 모든 내전 데이터가 초기화되었습니다. 모두 수고하셨습니다!");
    }

    private async Task ButtonExecutedAsync(SocketMessageComponent component)
    {
        var user = component.User as SocketGuildUser;
        if (user == null) return;

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
        else if (component.Data.CustomId == "move_attack" || component.Data.CustomId == "move_defense")
        {
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
                await user.ModifyAsync(x => x.Channel = targetChannel);
                await component.RespondAsync($"🏃 **[{targetChannel.Name}]** 채널로 이동되었습니다!", ephemeral: true);
            }
            catch (Exception ex)
            {
                await component.RespondAsync("❌ 채널 이동 도중 오류가 발생했습니다. 권한 설정을 확인하세요.", ephemeral: true);
                Console.WriteLine($"이동 오류: {ex.Message}");
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