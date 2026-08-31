using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;
    private Random _rand = new Random();

    // 🤬 비속어 및 금칙어 필터 목록
    private readonly string[] _badWords = 
    { 
        "애미", "고아", "시발", "씨발", "미친", "창년", "보지", "자지", "니엄마", "앙", 
        "ㅂ1ㅅ", "ㅄ", "ㅂㅅ", "씹새", "죽여버린다", "개새끼", "병신", "존나", "지랄", 
        "닥쳐", "애비", "느금마", "느금", "새끼", "꺼져", "엠창", "ㅗ", "시발럼", "씨발럼", 
        "개소리", "엠생", "썅", "개씨발", "좆", "엿먹어", "미친놈", "미친년", "지랄마",
        "장애", "장애인", "장애우"
    };

    // 💬 경고 메시지 목록
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

    // 🤖 끝말잇기 인정 단어장 (약 500개)
    private readonly List<string> _koreanWords = new List<string> 
    { 
        // ㄱ
        "가방", "가위", "가족", "가지", "각도", "간식", "갈매기", "감자", "갑옷", "강아지", "강물", "개미", "개구리", "거미", "거북이", "거울", 
        "건강", "건전지", "건축물", "걷기", "겨울", "결과", "경찰", "경치", "계란", "계산기", "고양이", "고구마", "고기", "고무줄", "고속도로", 
        "곤충", "골목", "곰", "곱셈", "공기", "공룡", "공부", "공원", "공책", "과자", "과일", "과학", "관람", "광장", "구름", "구두", "구멍", 
        "국수", "국어", "군인", "궁전", "귀걸이", "그림", "그림자", "근육", "글씨", "글자", "금붕어", "금요일", "기계", "기린", "기분", "기차", 
        "기침", "기타", "길거리", "김밥", "김치", "꼬리", "꽃병", "꽃다발", "꿈", "끝말잇기",
        // ㄴ
        "나비", "나무", "나머지", "나이", "나팔", "낙엽", "낚시", "날씨", "남동생", "남쪽", "냄새", "냉장고", "너구리", "넥타이", "노래", "노트북", 
        "놀이터", "농구", "눈사람", "뉴스", "늑대", "능력", "늪", "냄비",
        // ㄷ
        "다람쥐", "다리", "다이어트", "달력", "닭고기", "당근", "대나무", "대통령", "대화", "대회", "도달", "도서관", "도마뱀", "도토리", "독서", 
        "독수리", "돈", "돌멩이", "동굴", "동물원", "동전", "동화", "돼지", "된장", "두부", "드라마", "드레스", "드럼", "등산", "디저트", "딸기", "떡볶이", "뚜껑",
        // ㄹ
        "라디오", "라면", "라이터", "라일락", "람보르기니", "래퍼", "럭비", "레고", "레몬", "레스토랑", "레시피", "레일", "로봇", "로켓", "루비", 
        "리본", "리듬", "리모컨", "리무진", "리포터", "리필",
        // ㅁ
        "마술", "마이크", "마스크", "마을", "마음", "만두", "만화", "말", "맛", "망토", "망치", "매미", "매직", "머그컵", "머리카락", "먼지", 
        "메뚜기", "메모지", "메시지", "명함", "모기", "모니터", "모래", "모자", "목걸이", "목소리", "목적", "무당벌레", "무릎", "무지개", "문방구", 
        "문제", "물감", "물고기", "물방울", "미국", "미래", "미술", "밑바닥",
        // ㅂ
        "바나나", "바다", "바닥", "바람", "바구니", "바이올린", "박쥐", "박수", "반찬", "반지", "발자국", "밤하늘", "방패", "배구", "배낭", "배추", 
        "백조", "버스", "버섯", "번개", "벚꽃", "별", "병원", "보라색", "보석", "복숭아", "볼펜", "봄", "부채", "북극곰", "불꽃", "비밀", "비누", "비행기", "빵", "뼈",
        // ㅅ
        "사과", "사막", "사람", "사랑", "사슴", "사자", "사진", "사탕", "산길", "산타", "살구", "상자", "상어", "새싹", "새우", "샌드위치", "색연필", 
        "생강", "생물", "생활", "샴푸", "서랍", "서울", "선풍기", "선생님", "설탕", "성곽", "세계", "소금", "소나무", "소리", "소시지", "소파", "소화기", 
        "손가락", "수건", "수박", "수영장", "스마트폰", "스웨터", "스케이트", "스타킹", "스피커", "슬리퍼", "시간", "시계", "시장", "시민", "식당", "식물", "신발", "실과",
        // ㅇ
        "아기", "아빠", "아이스크림", "아침", "악기", "안경", "안전", "암호", "양말", "양파", "양치기", "얼굴", "얼음", "엄마", "에어컨", "엘리베이터", 
        "여름", "여우", "여행", "역사", "연필", "열쇠", "영어", "영화", "영웅", "오렌지", "오리", "오징어", "오토바이", "옷", "우산", "우유", "우주선", 
        "운동장", "운전", "웃음", "원숭이", "유리", "유치원", "은혜", "음악", "의자", "이름", "이빨", "인형", "일요일", "일기",
        // ㅈ
        "자전거", "자두", "자동차", "자석", "자연", "자음", "작가", "잔디", "장난감", "장미", "장화", "전구", "전기", "전화기", "점심", "접시", "정원", 
        "제목", "제비", "조건", "조개", "주머니", "주전자", "주스", "죽음", "준비", "지갑", "지구", "지우개", "지진", "지하철", "직업", "진주", "질문", "찜질방",
        // ㅊ
        "차도", "차이", "참새", "창문", "채소", "책상", "천사", "철도", "청바지", "체육", "초콜릿", "촛불", "촬영", "축구", "춤", "치즈", "치과", "치약", "치마", "친구", "칠판", "침대", "칫솔",
        // ㅋ, ㅌ
        "카메라", "카레", "카페", "카펫", "칼", "캐비닛", "캠핑", "커튼", "커피", "컴퓨터", "컵", "코끼리", "코트", "코코넛", "콜라", "콩", "크레파스", "크리스마스", "키보드", "키위",
        "타조", "타이어", "타악기", "탁자", "탄소", "탈출", "태양", "택시", "텐트", "텔레비전", "토끼", "토마토", "톱", "통장", "통조림", "트럭", "트럼펫", "티셔츠",
        // ㅍ, ㅎ
        "파도", "파리", "파인애플", "파티", "팔찌", "팥빙수", "팽이", "편지", "평화", "포도", "포크", "표범", "풍선", "피아노", "피자", "필통",
        "하늘", "학교", "학생", "학용품", "한국", "할머니", "할아버지", "해바라기", "햇빛", "햄버거", "향수", "허수아비", "혀", "호랑이", "호수", "호박", 
        "화가", "화분", "화장실", "환자", "활", "회의", "휴지", "휴대폰", "희망", "흰색"
    };

    // 🎯 DM 저격을 위한 메모리
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
        }
        catch (Exception) { }
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot) return;

        string content = message.Content.Trim();

        // 1️⃣ DM 저격 기능
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

            // 🧹 물음표(?), 느낌표(!), 공백 등 특수문자 제거 후 순수 한글만 추출
            string cleanContent = Regex.Replace(content, @"[^가-힣]", "");

            // 한글 2글자 미만 입력 시 거부
            if (cleanContent.Length < 2) 
            {
                await message.Channel.SendMessageAsync("❌ 2글자 이상의 한글 단어를 입력해 주세요!");
                return;
            }

            string currentWord = _wordChainChannels[message.Channel.Id];
            char lastChar = currentWord.Last();
            char firstChar = cleanContent.First();

            // 글자 이어지는지 검사
            if (lastChar != firstChar)
            {
                await message.Channel.SendMessageAsync($"❌ 땡! **'{lastChar}'**(으)로 시작하는 단어를 말하셔야죠!\n(그만하려면 `끝말잇기 종료`를 입력하세요)");
                return;
            }

            // 🔍 단어장에 등록되어 있는 단어인지 검사 (지어낸 단어 방지)
            if (!_koreanWords.Contains(cleanContent))
            {
                await message.Channel.SendMessageAsync($"❌ **'{cleanContent}'**(은)는 제가 모르는 단어이거나 지어낸 단어입니다! 다른 단어를 써주세요.");
                return;
            }

            // 봇의 응답 처리
            char newLastChar = cleanContent.Last();
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
                string startWord = _koreanWords[_rand.Next(_koreanWords.Count)];
                char lastChar = startWord.Last();
                
                _wordChainChannels[message.Channel.Id] = startWord;
                await message.Channel.SendMessageAsync($"🎮 끝말잇기 시작! 첫 단어는 **{startWord}**입니다.\n**'{lastChar}'**(으)로 시작하는 단어를 채팅으로 쳐주세요!");
            }
        }
    }
}