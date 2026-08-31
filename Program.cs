using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    private Random _rand = new Random();

    private readonly string[] _badWords = { "애미", "고아", "시발", "씨발", "미친" , "창년" ,"보지" ,"자지","니엄마","앙" };

    private readonly string[] _warnings = 
    {
        "어허 그런말하면 안됩니다...",
        "앗 그런말하면 못써요..!",
        "아니 지금 뭐라 하신거죠!? 당장 지우세요.",
        "진짜 그런말을 하시다니 실망이네요",
        "그런말을 하시다니 삐질게요 ㅠ"
    };

    private readonly Dictionary<ulong, string> _wordChainChannels = new Dictionary<ulong, string>();

    private readonly List<string> _koreanWords = new List<string> 
    { 
        "거미", "미술", "술래", "내과", "과일", "일요일", "일기", "기차", "차도", 
        "도토리", "리본", "본드", "드럼", "럼주", "주전자", "자전거", "거위", "위험", 
        "험난", "난기류", "류마티스", "스프", "프랑스", "스케치", "치즈", "즈믄" 
    };

    private readonly Dictionary<string, SocketMessage> _lastMessages = new Dictionary<string, SocketMessage>();

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages
        };
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync; 

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _ = Task.Run(() =>
        {
            try
            {
                string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
                var listener = new System.Net.HttpListener();
                listener.Prefixes.Add($"http://*:{port}/");
                listener.Start();
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
            catch (Exception) { }
        });

        await Task.Delay(-1);
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }

    private async Task ReadyAsync()
    {
        Console.WriteLine($"🤖 봇이 {_client.CurrentUser.Username} 이름으로 연결되었습니다!");

        // 🧹 이전 슬래시 명령어 전부 삭제
        try
        {
            await _client.Rest.DeleteAllGlobalCommandsAsync();
            foreach (var guild in _client.Guilds)
            {
                await guild.DeleteApplicationCommandsAsync();
            }
            Console.WriteLine("🧹 기존 슬래시 명령어 삭제 완료!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"명령어 삭제 중 오류: {ex.Message}");
        }
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot) return;

        string content = message.Content.Trim();

        if (message.Channel is SocketDMChannel)
        {
            string targetName = content;

            if (_lastMessages.ContainsKey(targetName))
            {
                var targetMsg = _lastMessages[targetName];
                try
                {
                    await targetMsg.Channel.SendMessageAsync("그런말 하지 마세요", messageReference: new MessageReference(targetMsg.Id));
                    await message.Channel.SendMessageAsync($"✅ 성공! 서버의 '{targetName}'님 최근 메시지에 저격 답장을 남겼습니다.");
                }
                catch (Exception)
                {
                    await message.Channel.SendMessageAsync("❌ 메시지를 보낼 수 없습니다.");
                }
            }
            else
            {
                await message.Channel.SendMessageAsync($"❌ 현재 봇의 기억장치에 '{targetName}'님이 최근에 친 채팅이 없습니다.");
            }
            return;
        }

        var guildUser = message.Author as SocketGuildUser;
        if (guildUser != null)
        {
            _lastMessages[guildUser.Username] = message;
            if (!string.IsNullOrEmpty(guildUser.Nickname)) _lastMessages[guildUser.Nickname] = message;
            if (!string.IsNullOrEmpty(guildUser.GlobalName)) _lastMessages[guildUser.GlobalName] = message;
        }

        if (_badWords.Any(word => content.Contains(word)))
        {
            string randomWarning = _warnings[_rand.Next(_warnings.Length)];
            await message.Channel.SendMessageAsync($"<@{message.Author.Id}> {randomWarning}");
            return; 
        }

        if (_wordChainChannels.ContainsKey(message.Channel.Id))
        {
            if (content == "끝말잇기 종료")
            {
                _wordChainChannels.Remove(message.Channel.Id);
                await message.Channel.SendMessageAsync("🛑 끝말잇기를 종료합니다!");
                return;
            }

            if (content.Length < 2) return;

            string currentWord = _wordChainChannels[message.Channel.Id];
            char lastChar = currentWord.Last();
            char firstChar = content.First();

            if (lastChar != firstChar)
            {
                await message.Channel.SendMessageAsync($"❌ 땡! **'{lastChar}'**(으)로 시작하는 단어를 말하셔야죠!\n(그만하려면 `끝말잇기 종료`를 입력하세요)");
                return;
            }

            char newLastChar = content.Last();
            var possibleWords = _koreanWords.Where(w => w.StartsWith(newLastChar.ToString())).ToList();

            if (possibleWords.Count > 0)
            {
                string botWord = possibleWords[_rand.Next(possibleWords.Count)];
                _wordChainChannels[message.Channel.Id] = botWord;
                await message.Channel.SendMessageAsync(botWord);
            }
            else
            {
                _wordChainChannels.Remove(message.Channel.Id);
                await message.Channel.SendMessageAsync($"앗... **'{newLastChar}'**(으)로 시작하는 단어를 모르겠어요. 제가 졌습니다! 🏳️\n(게임이 종료되었습니다)");
            }
        }
        else
        {
            if (content == "끝말잇기 시작")
            {
                string startWord = "자전거"; 
                _wordChainChannels[message.Channel.Id] = startWord;
                await message.Channel.SendMessageAsync($"🎮 끝말잇기 시작! 첫 단어는 **{startWord}**입니다.\n**'거'**로 시작하는 단어를 채팅으로 쳐주세요!");
            }
        }
    }
}