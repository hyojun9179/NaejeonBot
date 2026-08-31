using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

class Program
{
    private DiscordSocketClient _client;

    // 🎯 메시지를 전달받을 대상의 ID 설정
    private readonly ulong AnonymousTargetId = 852349769066348584; // 익명으로 받을 사람
    private readonly ulong IdentifiedTargetId = 1434903209331261611; // 실명(유저정보)으로 받을 사람

    // 🚫 차단할 유저 ID 목록 (희망이에 의해 차단됨)
    private readonly ulong[] _blockedUsers = 
    { 
        1259472924646309956, 
        1023108668654374962 
    };

    static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

    public async Task MainAsync()
    {
        var config = new DiscordSocketConfig
        {
            // 봇이 DM(개인 메시지)과 메시지 내용을 읽을 수 있도록 인텐트 활성화
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.DirectMessages
        };
        
        _client = new DiscordSocketClient(config);

        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;

        string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
        
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // 봇 꺼짐 방지용 웹 서버 (포트 리스너)
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
        Console.WriteLine($"🤖 봇이 {_client.CurrentUser.Username} 이름으로 연결되었습니다! (DM 전달 및 차단 모드 작동 중)");
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        // 봇 자신이 보낸 메시지이거나 다른 봇의 메시지는 무시
        if (message.Author.IsBot) return;

        // 💬 DM(개인 메시지) 채널인지 확인
        if (message.Channel is SocketDMChannel)
        {
            // 🛑 차단된 유저인지 검사
            if (_blockedUsers.Contains(message.Author.Id))
            {
                await message.Channel.SendMessageAsync("희망이에 의해 메세지 사용이 차단되었습니다.");
                return; // 여기서 더 이상 진행하지 않고 종료
            }

            string content = message.Content.Trim();
            
            // 사진, 영상 등 첨부파일이 같이 온 경우 처리
            if (message.Attachments.Count > 0)
            {
                string attachmentUrls = string.Join("\n", message.Attachments.Select(a => a.Url));
                content += $"\n\n[첨부파일]:\n{attachmentUrls}";
            }

            // 메시지 내용이나 첨부파일이 아예 없는 경우 무시
            if (string.IsNullOrEmpty(content)) return;

            try
            {
                // 1️⃣ 익명 대상에게 전송 (Rest.GetUserAsync를 사용해야 서버에 없어도 DM 전송 가능)
                var anonUser = await _client.Rest.GetUserAsync(AnonymousTargetId);
                if (anonUser != null)
                {
                    await anonUser.SendMessageAsync($"📩 **[익명 메시지]**\n\n{content}");
                }

                // 2️⃣ 실명(관리자 등) 대상에게 전송
                var idUser = await _client.Rest.GetUserAsync(IdentifiedTargetId);
                if (idUser != null)
                {
                    await idUser.SendMessageAsync($"🚨 **[{message.Author.Username} ({message.Author.Id})]** 님의 메시지\n\n{content}");
                }

                // 3️⃣ 발신자에게 전송 완료 안내
                await message.Channel.SendMessageAsync("✅ 메시지가 성공적으로 전달되었습니다.");
            }
            catch (Exception ex)
            {
                // 상대방이 봇의 DM을 차단했거나, 봇과 겹치는 서버가 하나도 없을 때 발생하는 오류 방어
                Console.WriteLine($"[DM 전송 오류]: {ex.Message}");
                await message.Channel.SendMessageAsync("❌ 메시지 전달 중 오류가 발생했습니다. (받는 사람이 DM을 막아두었을 수 있습니다.)");
            }
        }
    }
}