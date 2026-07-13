using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    private readonly Dictionary<ulong, List<ulong>> _naejeonParticipants = new Dictionary<ulong, List<ulong>>();

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.SlashCommandExecuted += SlashCommandHandler;
        _client.ButtonExecuted += ButtonExecutedAsync;

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _ = Task.Run(() =>
        {
            try
            {
                string port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
                var listener = new System.Net.HttpListener();
                listener.Prefixes.Add($"http://+:{port}/");
                listener.Start();
                while (true) { var context = listener.GetContext(); context.Response.StatusCode = 200; context.Response.Close(); }
            }
            catch { }
        });

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log) { Console.WriteLine(log.ToString()); return Task.CompletedTask; }

    private async Task ReadyAsync()
    {
        var guildCommand = new SlashCommandBuilder()
            .WithName("내전").WithDescription("내전 모집글을 생성합니다.")
            .AddOption("날짜", ApplicationCommandOptionType.String, "날짜", true)
            .AddOption("시간", ApplicationCommandOptionType.String, "시간", true)
            .AddOption("몇시간뒤", ApplicationCommandOptionType.Integer, "숫자", true)
            .AddOption("내용", ApplicationCommandOptionType.String, "내용", true);

        var deleteCommand = new SlashCommandBuilder()
            .WithName("청소").WithDescription("메시지 삭제")
            .AddOption("개수", ApplicationCommandOptionType.Integer, "개수 (1~100)", true);

        await _client.CreateGlobalApplicationCommandAsync(guildCommand.Build());
        await _client.CreateGlobalApplicationCommandAsync(deleteCommand.Build());
    }

    private async Task SlashCommandHandler(SocketSlashCommand command)
    {
        // 핵심: 명령어 수신 즉시 Defer 처리하여 로딩 방지
        await command.DeferAsync(command.CommandName == "청소");

        if (command.CommandName == "청소")
        {
            var count = (int)(long)command.Data.Options.First().Value;
            var messages = await command.Channel.GetMessagesAsync(count).FlattenAsync();
            await ((ITextChannel)command.Channel).DeleteMessagesAsync(messages);
            await command.FollowupAsync("✅ 삭제 완료", ephemeral: true);
            return;
        }

        if (command.CommandName == "내전")
        {
            var options = command.Data.Options.ToList();
            string date = options[0].Value.ToString();
            string time = options[1].Value.ToString();
            long timer = (long)options[2].Value;
            string content = options[3].Value.ToString();

            var embed = new EmbedBuilder()
                .WithTitle("#내전 모집")
                .WithDescription($"일시: {date} {time}\n내용: {content}")
                .WithColor(Color.Orange)
                .Build();

            var component = new ComponentBuilder().WithButton("참여하기", "join_naejeon", ButtonStyle.Success).Build();
            var msg = await command.FollowupAsync(embed: embed, components: component);
            _naejeonParticipants[msg.Id] = new List<ulong>();

            _ = Task.Run(async () => {
                await Task.Delay((int)timer * 3600000);
                // 여기에 기존 DM 알림 로직 유지
                await SendNaejeonManageDM(command.User, msg.Id, command.Channel.Id);
            });
        }
    }

    private async Task ButtonExecutedAsync(SocketMessageComponent comp)
    {
        if (comp.Data.CustomId == "join_naejeon")
        {
            if (!_naejeonParticipants[comp.Message.Id].Contains(comp.User.Id))
            {
                _naejeonParticipants[comp.Message.Id].Add(comp.User.Id);
                await comp.RespondAsync("✅ 등록 완료", ephemeral: true);
            }
            else await comp.RespondAsync("이미 등록됨", ephemeral: true);
        }
    }

    private async Task SendNaejeonManageDM(IUser host, ulong msgId, ulong chanId) { /* 기존 DM 로직 그대로 유지 */ }
}