using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing;
using System.IO;

namespace DiscordBingoBot
{
    class Program
    {
        private DiscordSocketClient _client;
        private BingoGame _game;
        private Dictionary<ulong, DateTime> _gameStartTimes = new Dictionary<ulong, DateTime>();

        static async Task Main(string[] args)
        {
            var program = new Program();
            await program.MainAsync();
        }

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                   | GatewayIntents.GuildMessages
                   | GatewayIntents.MessageContent
                   | GatewayIntents.DirectMessages
                   | GatewayIntents.GuildMessageReactions
                   | GatewayIntents.GuildVoiceStates
            });

            _client.Log += LogAsync;
            _client.MessageReceived += MessageReceivedAsync;
            _client.UserVoiceStateUpdated += UserVoiceStateUpdatedAsync;

            Console.Write("Enter API Token: ");
            string token = Console.ReadLine();

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            _game = new BingoGame(_client);

            // Initialize command UI
            Console.Title = "Discord Bingo Bot";
            DrawConsoleUI();

            await Task.Delay(-1);
        }

        private void DrawConsoleUI()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════════╗");
            Console.WriteLine("║       DISCORD BINGO BOT - ONLINE       ║");
            Console.WriteLine("╚════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine("\nCommands Log:");
        }

        private void LogCommand(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{DateTime.Now:T}] {message}");
            Console.ResetColor();
        }

        private void LogGameEvent(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[{DateTime.Now:T}] GAME: {message}");
            Console.ResetColor();
        }

        private async Task UserVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            if (!_game.IsStarted && user is SocketGuildUser guildUser)
            {
                // Check if user left the required voice channel before game start
                if (oldState.VoiceChannel != null && newState.VoiceChannel == null)
                {
                    var player = _game.Players.Find(p => p.User.Id == user.Id);
                    if (player != null)
                    {
                        await _game.RemovePlayerAsync(user);
                        LogCommand($"{user.Username} left voice channel and was removed from game");
                    }
                }
            }
        }

        private Task LogAsync(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            var args = msg.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = args.Length > 1 ? args[1].ToLower() : "";

            if (msg.Content.StartsWith("!bingo", StringComparison.OrdinalIgnoreCase))
            {
                LogCommand($"{msg.Author.Username} used command: {msg.Content}");

                switch (cmd)
                {
                    case "join":
                        await HandleJoinCommand(msg);
                        break;

                    case "start":
                        await HandleStartCommand(msg);
                        break;

                    case "stop":
                        await HandleStopCommand(msg);
                        break;

                    case "mark":
                        await HandleMarkCommand(msg, args);
                        break;

                    case "stats":
                        await HandleStatsCommand(msg);
                        break;

                    case "help":
                    default:
                        await HandleHelpCommand(msg);
                        break;
                }
            }

            if (msg.Content.Contains("bingo!", StringComparison.OrdinalIgnoreCase))
            {
                LogCommand($"{msg.Author.Username} called BINGO!");
                await _game.WinnerChecker(msg.Author as SocketUser, msg.Channel);
            }
        }

        private async Task HandleJoinCommand(SocketMessage msg)
        {
            if (_game.IsStarted)
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Username}, the game already started. Wait for the next round.");
                return;
            }

            // Check if user is in a voice channel
            var guildUser = msg.Author as SocketGuildUser;
            if (guildUser?.VoiceChannel == null)
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Username}, you must join a voice channel first before joining the game!");
                return;
            }

            await _game.AddPlayerAsync(msg.Author as SocketUser, msg.Channel);
        }

        private async Task HandleStartCommand(SocketMessage msg)
        {
            if (_game.IsStarted)
            {
                await msg.Channel.SendMessageAsync("Game already started.");
                return;
            }

            if (_game.Players.Count < 1)
            {
                await msg.Channel.SendMessageAsync("No players joined yet! Use `!bingo join` to join.");
                return;
            }

            // Record game start time
            _gameStartTimes[msg.Channel.Id] = DateTime.Now;
            LogGameEvent($"Game started by {msg.Author.Username} in channel {msg.Channel.Name}");

            await _game.StartGameAsync(msg.Channel);
        }

        private async Task HandleStopCommand(SocketMessage msg)
        {
            if (!_game.IsStarted)
            {
                await msg.Channel.SendMessageAsync("No game is currently running.");
                return;
            }

            LogGameEvent($"Game stopped by {msg.Author.Username}");
            _game.StopGame();
            await msg.Channel.SendMessageAsync("Game has been stopped.");
        }

        private async Task HandleMarkCommand(SocketMessage msg, string[] args)
        {
            if (args.Length < 3)
            {
                await msg.Channel.SendMessageAsync("Usage: `!bingo mark <BINGONUMBER>` e.g. `!bingo mark B10`");
                return;
            }

            await _game.MarkNumberAsync(msg.Author as SocketUser, args[2].ToUpper(), msg.Channel);
        }

        private async Task HandleStatsCommand(SocketMessage msg)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Bingo Game Stats")
                .WithColor(Discord.Color.Blue)
                .WithCurrentTimestamp();

            if (_game.IsStarted)
            {
                var duration = DateTime.Now - _gameStartTimes[msg.Channel.Id];
                embed.AddField("Status", "Game in progress", true)
                     .AddField("Duration", $"{duration:mm\\:ss}", true)
                     .AddField("Players", _game.Players.Count, true)
                     .AddField("Numbers Called", _game.CalledNumbers.Count, true)
                     .AddField("Last Number", _game.CalledNumbers.LastOrDefault()?.ToString() ?? "None", true);
            }
            else
            {
                embed.WithDescription("No game is currently running");
            }

            if (_game.PreviousWinners.Count > 0)
            {
                embed.AddField("Previous Winners", string.Join("\n", _game.PreviousWinners.Select(w => $"- {w.Username}")));
            }

            await msg.Channel.SendMessageAsync(embed: embed.Build());
        }

        private async Task HandleHelpCommand(SocketMessage msg)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Bingo Bot Help")
                .WithColor(Discord.Color.Green)
                .AddField("How to Play", "Join a voice channel, then use the commands below to play!")
                .AddField("Commands",
                    "`!bingo join` - Join the game (must be in voice channel)\n" +
                    "`!bingo start` - Start the game\n" +
                    "`!bingo mark <BINGONUMBER>` - Mark a number on your card\n" +
                    "`!bingo stats` - Show game statistics\n" +
                    "`!bingo stop` - Stop the game\n" +
                    "`!bingo help` - Show this message\n" +
                    "`bingo!` - Call when you have a complete line!")
                .WithFooter("Have fun playing Bingo!");

            await msg.Channel.SendMessageAsync(embed: embed.Build());
        }
    }

    public class BingoGame : IDisposable
    {
        private readonly DiscordSocketClient _client;
        private readonly Random _rand = new();
        private System.Threading.Timer? _numberCallTimer;
        private ITextChannel? _gameChannel;

        public List<BingoPlayer> Players { get; } = new();
        public List<SocketUser> PreviousWinners { get; } = new();
        public List<BingoNumber> CalledNumbers { get; } = new();
        public bool IsStarted { get; private set; } = false;
        public bool IsWinner { get; private set; } = false;

        public BingoGame(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task AddPlayerAsync(SocketUser user, IMessageChannel channel)
        {
            if (Players.Exists(p => p.User.Id == user.Id))
            {
                await channel.SendMessageAsync($"{user.Username}, you already joined.");
                return;
            }

            Players.Add(new BingoPlayer(user));
            await channel.SendMessageAsync($"{user.Username} joined the game!");
        }

        public async Task RemovePlayerAsync(SocketUser user)
        {
            var player = Players.Find(p => p.User.Id == user.Id);
            if (player != null)
            {
                Players.Remove(player);
                await _gameChannel?.SendMessageAsync($"{user.Username} was removed from the game (left voice channel)");
            }
        }

        public async Task StartGameAsync(IMessageChannel channel)
        {
            IsStarted = true;
            _gameChannel = channel as ITextChannel;
            CalledNumbers.Clear();

            foreach (var player in Players)
            {
                player.GenerateCard();

                var dmChannel = await player.User.CreateDMChannelAsync();
                var imgBytes = BingoImageGenerator.GenerateBingoCardImage(player.Card, player.Marked);

                using var stream = new MemoryStream(imgBytes);
                var file = new FileAttachment(stream, "bingo_card.png");

                await dmChannel.SendFileAsync(file, "Here is your Bingo card! Use `!bingo mark <BINGONUMBER>` to mark numbers.");
            }

            await _gameChannel.SendMessageAsync($"Game started with {Players.Count} player(s)! Numbers will be called every 15 seconds.");

            _numberCallTimer = new System.Threading.Timer(async _ => await CallNumberAsync(), null, 0, 15000);
        }

        public void StopGame()
        {
            IsStarted = false;
            _numberCallTimer?.Dispose();
            _gameChannel = null;
            Players.Clear();
        }

        public async Task WinnerChecker(SocketUser user, IMessageChannel channel)
        {
            var player = Players.Find(p => p.User.Id == user.Id);
            if (player == null)
            {
                await channel.SendMessageAsync($"{user.Username}, you're not part of this game!");
                return;
            }

            if (player.HasBingo())
            {
                IsWinner = true;
                PreviousWinners.Add(user);
                _numberCallTimer?.Dispose();

                var embed = new EmbedBuilder()
                    .WithTitle("🎉 BINGO! 🎉")
                    .WithDescription($"{user.Username} has won the game!")
                    .WithColor(Discord.Color.Gold)
                    .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithCurrentTimestamp();

                await _gameChannel.SendMessageAsync(embed: embed.Build());
                IsStarted = false;

                // Send winning card
                var imgBytes = BingoImageGenerator.GenerateBingoCardImage(player.Card, player.Marked, true);
                using var stream = new MemoryStream(imgBytes);
                var file = new FileAttachment(stream, "winning_card.png");
                await _gameChannel.SendFileAsync(file, $"{user.Username}'s winning card!");
            }
            else
            {
                await channel.SendMessageAsync($"{user.Username}, you don't have BINGO yet! Keep playing!");
            }
        }

        private async Task CallNumberAsync()
        {
            if (CalledNumbers.Count >= 75)
            {
                _numberCallTimer?.Dispose();
                await _gameChannel?.SendMessageAsync("All numbers have been called. Game over.");
                IsStarted = false;
                return;
            }

            BingoNumber number;
            do
            {
                int num = _rand.Next(1, 76);
                number = new BingoNumber(num);
            } while (CalledNumbers.Any(n => n.Number == number.Number));

            CalledNumbers.Add(number);
            await _gameChannel?.SendMessageAsync($"Number called: **{number}**");
        }

        public async Task MarkNumberAsync(SocketUser user, string bingoNumber, IMessageChannel channel)
        {
            if (!IsStarted)
            {
                await channel.SendMessageAsync("Game is not started yet.");
                return;
            }

            var player = Players.Find(p => p.User.Id == user.Id);
            if (player == null)
            {
                await channel.SendMessageAsync("You are not part of the game. Use `!bingo join` to join.");
                return;
            }

            if (!CalledNumbers.Any())
            {
                await channel.SendMessageAsync("No numbers have been called yet.");
                return;
            }

            if (bingoNumber.Length < 2)
            {
                await channel.SendMessageAsync("Invalid bingo number format. Example: B10");
                return;
            }

            char letter = bingoNumber[0];
            if (!"BINGO".Contains(letter))
            {
                await channel.SendMessageAsync("Invalid bingo column letter. Use B, I, N, G, or O.");
                return;
            }

            if (!int.TryParse(bingoNumber[1..], out int number))
            {
                await channel.SendMessageAsync("Invalid number after letter.");
                return;
            }

            if (!CalledNumbers.Any(n => n.Number == number))
            {
                await channel.SendMessageAsync("That number has not been called yet.");
                return;
            }

            bool marked = player.MarkNumber(letter, number);
            var dmChannel = await user.CreateDMChannelAsync();

            if (marked)
            {
                await dmChannel.SendMessageAsync($"Number {bingoNumber} marked on your card.");

                var updatedImg = BingoImageGenerator.GenerateBingoCardImage(player.Card, player.GetMarkedGrid());
                using var imgStream = new MemoryStream(updatedImg);
                var updatedFile = new FileAttachment(imgStream, "updated_bingo_card.png");
                await dmChannel.SendFileAsync(updatedFile, $"Here is your updated Bingo card after marking {bingoNumber}.");
            }
            else
            {
                await dmChannel.SendMessageAsync($"{user.Username}, number {bingoNumber} is not on your card or already marked.");
            }
        }

        public void Dispose()
        {
            _numberCallTimer?.Dispose();
        }
    }

    public class BingoNumber
    {
        public int Number { get; }
        public char Letter { get; }

        public BingoNumber(int number)
        {
            Number = number;
            Letter = GetLetterForNumber(number);
        }

        private char GetLetterForNumber(int num)
        {
            if (num >= 1 && num <= 15) return 'B';
            else if (num <= 30) return 'I';
            else if (num <= 45) return 'N';
            else if (num <= 60) return 'G';
            else return 'O';
        }

        public override string ToString()
        {
            return $"{Letter}{Number}";
        }
    }

    public class BingoPlayer
    {
        public SocketUser User { get; }
        public int[,] Card { get; private set; } = new int[5, 5];
        public bool[,] Marked { get; } = new bool[5, 5];

        public BingoPlayer(SocketUser user)
        {
            User = user;
        }

        public bool[,] GetMarkedGrid() => (bool[,])Marked.Clone();

        public void GenerateCard()
        {
            Random rand = new();
            for (int col = 0; col < 5; col++)
            {
                int start = col * 15 + 1;
                List<int> nums = Enumerable.Range(start, 15).OrderBy(x => rand.Next()).Take(5).ToList();
                for (int row = 0; row < 5; row++)
                {
                    Card[row, col] = nums[row];
                    Marked[row, col] = false;
                }
            }
            Card[2, 2] = 0;
            Marked[2, 2] = true;
        }

        public bool MarkNumber(char letter, int number)
        {
            int col = "BINGO".IndexOf(letter);
            if (col == -1) return false;

            for (int row = 0; row < 5; row++)
            {
                if (Card[row, col] == number && !Marked[row, col])
                {
                    Marked[row, col] = true;
                    return true;
                }
            }
            return false;
        }

        public bool HasBingo()
        {
            // Check rows
            for (int r = 0; r < 5; r++)
            {
                if (Enumerable.Range(0, 5).All(c => Marked[r, c])) return true;
            }

            // Check columns
            for (int c = 0; c < 5; c++)
            {
                if (Enumerable.Range(0, 5).All(r => Marked[r, c])) return true;
            }

            // Check diagonals
            if (Enumerable.Range(0, 5).All(i => Marked[i, i])) return true;
            if (Enumerable.Range(0, 5).All(i => Marked[i, 4 - i])) return true;

            return false;
        }
    }

    public static class BingoImageGenerator
    {
        public static byte[] GenerateBingoCardImage(int[,] card, bool[,] marked, bool isWinner = false)
        {
            int width = 500;
            int height = 600;
            int cellSize = 90;
            int margin = 20;


            FontCollection fonts = new();
            FontFamily arial = fonts.Add("Resources/arial.ttf");
            Font headerFont = arial.CreateFont(36, FontStyle.Bold);
            Font numberFont = arial.CreateFont(24, FontStyle.Bold);
            Font winnerFont = arial.CreateFont(32, FontStyle.Bold);

            using Image<Rgba32> image = new(width, height);
            image.Mutate(ctx =>
            {
                if (isWinner)
                {
                    var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        new PointF(width, height),
                        GradientRepetitionMode.None,
                        new ColorStop(0, SixLabors.ImageSharp.Color.Gold),
                        new ColorStop(1, SixLabors.ImageSharp.Color.OrangeRed)
                    );
                    ctx.Fill(gradientBrush, new RectangleF(0, 0, width, height));
                }
                else
                {
                    ctx.Fill(SixLabors.ImageSharp.Color.White);
                }

                // Draw header "BINGO"
                string[] headers = { "B", "I", "N", "G", "O" };
                for (int col = 0; col < 5; col++)
                {
                    var rect = new RectangleF(margin + col * cellSize, margin, cellSize, cellSize);
                    ctx.Fill(SixLabors.ImageSharp.Color.DarkBlue, rect);
                    ctx.Draw(SixLabors.ImageSharp.Color.Black, 1, rect);
                    ctx.DrawText(headers[col], headerFont, SixLabors.ImageSharp.Color.White, rect.Center());
                }

                // Draw numbers
                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        var rect = new RectangleF(margin + col * cellSize, margin + (row + 1) * cellSize, cellSize, cellSize);
                        bool isMarked = marked[row, col];

                        SixLabors.ImageSharp.Color fillColor = isWinner && isMarked
                            ? SixLabors.ImageSharp.Color.Gold
                            : isMarked ? SixLabors.ImageSharp.Color.LightGreen : SixLabors.ImageSharp.Color.LightYellow;

                        ctx.Fill(fillColor, rect);
                        ctx.Draw(SixLabors.ImageSharp.Color.Black, 1, rect);

                        string text = card[row, col] == 0 ? "FREE" : card[row, col].ToString();
                        SixLabors.ImageSharp.Color textColor = isMarked ? SixLabors.ImageSharp.Color.Red : SixLabors.ImageSharp.Color.Black;

                        ctx.DrawText(text, numberFont, textColor, rect.Center());
                    }
                }

                if (isWinner)
                {
                    var location = new PointF(width / 2 - 80, height - 50);
                    ctx.DrawText("WINNER!", winnerFont, SixLabors.ImageSharp.Color.DarkRed, location);
                }
            });

            using MemoryStream ms = new();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        private static PointF Center(this RectangleF rect)
        {
            return new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
    }
}