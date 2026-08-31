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

    // 🤬 반응할 이상한/나쁜 단어들 (여기에 원하는 단어를 추가하세요)
    private readonly string[] _badWords = { "애미", "고아", "시발", "씨발", "미친" , "창년" ,"보지" ,"자지","니엄마","앙"};
    //이상한 말을 하는게 아니라 필터링봇 만드는겁니다 오해 맗아주세요

    // 💬 랜덤으로 출력할 예시 문장들
    private readonly string[] _warnings = 
    {
        "어허 그런말하면 안됩니다...",
        "앗 그런말하면 못써요..!",
        "아니 지금 뭐라 하신거죠!? 당장 지우세요.",
        "진짜 그런말을 하시다니 실망이네요",
        "그런말을 하시다니 삐질게요 ㅠ"
    };

    // 🔠 끝말잇기 진행 상태 저장 (채널 ID -> 현재 단어)
    private readonly Dictionary<ulong, string> _wordChainChannels = new Dictionary<ulong, string>();

    // 🤖 봇이 알고 있는 끝말잇기 단어장 (자유롭게 더 추가하세요)
    private readonly List<string> _koreanWords = new List<string> 
    { 
        "거미", "미술", "술래", "내과", "과일", "일요일", "일기", "기차", "차도", 
        "도토리", "리본", "본드", "드럼", "럼주", "주전자", "자전거", "거위", "위험", 
        "험난", "난기류", "류마티스", "스프", "프랑스", "스케치", "치즈", "즈믄" 
    };

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        // 💡 [중요] 유저의 채팅을 읽기 위해 MessageContent 인텐트가 반드시 필요합니다.
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMessages
        };
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync; // 채팅 감지 이벤트 추가

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // Render 서버 다운 방지용 웹서버
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

    private Task ReadyAsync()
    {
        Console.WriteLine($"🤖 봇이 { _client.CurrentUser.Username } 이름으로 연결되었습니다!");
        return Task.CompletedTask;
    }

    // 💬 채팅이 올라올 때마다 실행되는 로직
    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // 봇이 친 채팅은 무시
        if (message.Author.IsBot) return;

        string content = message.Content.Trim();

        // 1️⃣ 이상한 말(금칙어) 필터링 로직
        if (_badWords.Any(word => content.Contains(word)))
        {
            string randomWarning = _warnings[_rand.Next(_warnings.Length)];
            await message.Channel.SendMessageAsync($"<@{message.Author.Id}> {randomWarning}");
            return; // 경고를 줬다면 끝말잇기 로직은 무시함
        }

        // 2️⃣ 끝말잇기 게임 로직
        if (_wordChainChannels.ContainsKey(message.Channel.Id))
        {
            if (content == "끝말잇기 종료")
            {
                _wordChainChannels.Remove(message.Channel.Id);
                await message.Channel.SendMessageAsync("🛑 끝말잇기를 종료합니다!");
                return;
            }

            // 1글자 단어나 명령어가 아닌 일반 채팅은 무시
            if (content.Length < 2) return;

            string currentWord = _wordChainChannels[message.Channel.Id];
            char lastChar = currentWord.Last();
            char firstChar = content.First();

            // 글자가 이어지는지 확인 (두음법칙은 생략된 단순 비교)
            if (lastChar != firstChar)
            {
                await message.Channel.SendMessageAsync($"❌ 땡! **'{lastChar}'**(으)로 시작하는 단어를 말하셔야죠!\n(그만하려면 `끝말잇기 종료`를 입력하세요)");
                return;
            }

            // 봇이 대답할 단어 찾기
            char newLastChar = content.Last();
            var possibleWords = _koreanWords.Where(w => w.StartsWith(newLastChar.ToString())).ToList();

            if (possibleWords.Count > 0)
            {
                // 아는 단어가 있으면 랜덤으로 하나 골라서 대답
                string botWord = possibleWords[_rand.Next(possibleWords.Count)];
                _wordChainChannels[message.Channel.Id] = botWord;
                await message.Channel.SendMessageAsync(botWord);
            }
            else
            {
                // 아는 단어가 없으면 봇의 패배
                _wordChainChannels.Remove(message.Channel.Id);
                await message.Channel.SendMessageAsync($"앗... **'{newLastChar}'**(으)로 시작하는 단어를 모르겠어요. 제가 졌습니다! 🏳️\n(게임이 종료되었습니다)");
            }
        }
        else
        {
            // 게임이 진행 중이지 않을 때 시작 명령어 감지
            if (content == "끝말잇기 시작")
            {
                string startWord = "자전거"; 
                _wordChainChannels[message.Channel.Id] = startWord;
                await message.Channel.SendMessageAsync($"🎮 끝말잇기 시작! 첫 단어는 **{startWord}**입니다.\n**'거'**로 시작하는 단어를 채팅으로 쳐주세요!");
            }
        }
    }
}