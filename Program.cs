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

    // 🤬 이상한/나쁜 단어 필터 목록
// 🤬 이상한/나쁜 단어 필터 목록
    private readonly string[] _badWords = 
    { 
        "애미", "고아", "시발", "씨발", "미친", "창년", "보지", "자지", "니엄마", "앙", 
        "ㅂ1ㅅ", "ㅄ", "ㅂㅅ", "씹새", "죽여버린다", "개새끼", "병신", "존나", "지랄", 
        "닥쳐", "애비", "느금마", "느금", "새끼", "꺼져", "엠창", "ㅗ", "시발럼", "씨발럼", 
        "개소리", "엠생", "썅", "개씨발", "좆", "엿먹어", "미친놈", "미친년", "지랄마",
        "장애", "장애인", "장애우"
    };

    // 💬 경고 메시지 예시 목록
    private readonly string[] _warnings = 
    {
        "어허 그런말하면 안됩니다...",
        "앗 그런말하면 못써요..!",
        "아니 지금 뭐라 하신거죠!? 당장 지우세요.",
        "진짜 그런말을 하시다니 실망이네요",
        "그런말을 하시다니 삐질게요 ㅠ"
    };

    // 🔠 끝말잇기 채널 상태 저장
    private readonly Dictionary<ulong, string> _wordChainChannels = new Dictionary<ulong, string>();

    // 🤖 끝말잇기 대폭 확충 단어장
    private readonly List<string> _koreanWords = new List<string> 
    { 
        // ㄱ
        "가방", "가수", "가위", "가족", "각도", "간식", "갈매기", "감자", "갑옷", "강아지", 
        "개발자", "개미", "개구리", "거미", "건전지", "경찰", "고양이", "고구마", "곤충", "공룡",
        "과일", "관람", "광장", "구름", "구두", "국수", "군인", "궁전", "귀걸이", "글자", 
        "금붕어", "기린", "기차", "기타", "길거리", "김치", "꽃병", "꽃다발", "기적", "기압",

        // ㄴ
        "나비", "나무", "나팔", "낙엽", "난초", "날씨", "남쪽", "냉장고", "너구리", "넥타이", 
        "노래", "노트북", "놀이터", "농구", "눈사람", "뉴스", "늑대", "느티나무", "난기류", "능력",

        // ㄷ
        "다람쥐", "다리", "달력", "당근", "대나무", "대통령", "대왕", "도서관", "도토리", "독수리", 
        "동굴", "동물원", "돼지", "두부", "드레스", "드럼", "딸기", "떡볶이", "도둑", "드라이버",

        // ㄹ
        "라디오", "라면", "라이터", "라벤더", "라임", "라이플", "러닝", "레몬", "레고", "레드", 
        "레슬링", "로봇", "로켓", "리본", "리듬", "리무진", "리포터", "리필", "류트", "럭비",

        // ㅁ
        "마술", "마이크", "마스크", "마을", "만화", "말티즈", "망치", "매직", "머그컵", "메모지", 
        "메뚜기", "명함", "모자", "모니터", "목걸이", "무지개", "무당벌레", "문방구", "물고기", "미술",

        // ㅂ
        "바나나", "바다", "바람", "바구니", "박수", "발자국", "밤하늘", "방패", "배구", "배낭", 
        "버스", "버섯", "번개", "보석", "보라색", "볼펜", "부채", "비행기", "비누", "병원",

        // ㅅ
        "사과", "사자", "사탕", "산길", "산타", "상자", "새싹", "새우", "선풍기", "선장", 
        "설탕", "성곽", "소나무", "소화기", "수박", "수영장", "스마트폰", "스프", "스케이트", "스피커",

        // ㅇ
        "아이스크림", "악기", "안경", "양말", "양파", "어부", "어린이", "얼음", "에어컨", "엘리베이터", 
        "여우", "연필", "영웅", "오징어", "오토바이", "우주선", "우산", "유치원", "인형", "일요일",

        // ㅈ
        "자전거", "자두", "자동차", "자석", "장난감", "장미", "전화기", "전구", "접시", "정원", 
        "제비", "조개", "주전자", "주스", "지우개", "지갑", "지진", "지하철", "직업", "진주",

        // ㅊ
        "창문", "차도", "채소", "천사", "철도", "청바지", "초콜릿", "촛불", "축구", "치즈", 
        "치약", "침대", "치마", "치료",

        // ㅋ, ㅌ, ㅍ, ㅎ
        "카메라", "카레", "카페", "캐비닛", "커튼", "컴퓨터", "코끼리", "코코넛", "키보드", "타악기", 
        "타이어", "택시", "텀블러", "테니스", "토끼", "통조림", "트럭", "트럼펫", "티라노사우루스", "티셔츠", 
        "파인애플", "파리", "피아노", "피자", "필통", "하늘", "호랑이", "호박", "해바라기", "휴대폰"
    };

    // 🎯 DM 저격을 위한 유저 메시지 메모리
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

        // 1️⃣ 봇 DM 저격
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

        // 2️⃣ 서버 채팅 유저 기억
        var guildUser = message.Author as SocketGuildUser;
        if (guildUser != null)
        {
            _lastMessages[guildUser.Username] = message;
            if (!string.IsNullOrEmpty(guildUser.Nickname)) _lastMessages[guildUser.Nickname] = message;
            if (!string.IsNullOrEmpty(guildUser.GlobalName)) _lastMessages[guildUser.GlobalName] = message;
        }

        // 3️⃣ 금칙어 감지
        if (_badWords.Any(word => content.Contains(word)))
        {
            string randomWarning = _warnings[_rand.Next(_warnings.Length)];
            await message.Channel.SendMessageAsync($"<@{message.Author.Id}> {randomWarning}");
            return; 
        }

        // 4️⃣ 끝말잇기 게임
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