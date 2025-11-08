using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dotnetProject.Models;
using dotnetProject.Services;
using Microsoft.Extensions.DependencyInjection; // <-- ADD THIS NAMESPACE

namespace dotnetProject.Hubs
{
    public enum GameState { Betting, Spinning, Result }

    public class RouletteHub : Hub
    {
        private static readonly ConcurrentDictionary<string, LobbyInfo> Lobbies = new();
        private readonly IWalletService _walletService;
        private readonly IServiceScopeFactory _scopeFactory; // <-- ADD THIS

        // Constructor: Inject IServiceScopeFactory
        public RouletteHub(IWalletService walletService, IServiceScopeFactory scopeFactory) // <-- ADD THIS
        {
            _walletService = walletService;
            _scopeFactory = scopeFactory; // <-- ADD THIS
        }

        public async Task JoinLobby(string lobbyCode, string playerName)
        {
            var httpContext = Context.GetHttpContext();
            var playerId = httpContext?.Items["PlayerId"]?.ToString() ?? Guid.NewGuid().ToString("N");
            await _walletService.GetOrCreatePlayerAsync(playerId, playerName);
            var lobby = Lobbies.GetOrAdd(lobbyCode, _ => {
                var newLobby = new LobbyInfo(
                    lobbyCode,
                    (IHubContext<RouletteHub>)Context.GetHttpContext().RequestServices.GetService(typeof(IHubContext<RouletteHub>)),
                    _scopeFactory // <-- PASS THE FACTORY, NOT THE SERVICE
                );
                newLobby.CreatorConnectionId = Context.ConnectionId;
                return newLobby;
            });

            var player = lobby.Players.FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));

            if (player != null) // Player is reconnecting
            {
                player.ConnectionId = Context.ConnectionId;
                player.Name = playerName;
            }
            else // This is a new player
            {
                // Get balance from wallet (this is safe, it's in the hub scope)
                var balance = await _walletService.GetBalanceAsync(playerId);

                player = new PlayerInfo
                {
                    ConnectionId = Context.ConnectionId,
                    Name = playerName,
                    PlayerId = playerId
                };
                lobby.Players.Add(player);
                lobby.Balances[playerId] = balance;
            }

            string notificationMessage = $"{playerName} has connected.";

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);
            await Clients.OthersInGroup(lobbyCode).SendAsync("PlayerJoined", notificationMessage);

            await Clients.Caller.SendAsync("SetConnectionId", Context.ConnectionId);
            var playerNames = lobby.Players.Select(p => p.Name).ToList();
            await Clients.Group(lobbyCode).SendAsync("UpdatePlayerList", playerNames, lobby.CreatorConnectionId);

            if (lobby.IsGameStarted)
            {
                await Clients.Caller.SendAsync("GameStarted", lobbyCode);
            }

            var leaderboard = lobby.Players
                .Select(p => new { Name = p.Name, Balance = lobby.Balances.GetValueOrDefault(p.PlayerId, 0) })
                .OrderByDescending(x => x.Balance)
                .ToList();
            await Clients.Caller.SendAsync("InitState", lobby.History, leaderboard, lobby.State, lobby.Countdown);
        }

        public async Task PlaceBet(string lobbyCode, BetInfo incomingBet)
        {
            if (!Lobbies.TryGetValue(lobbyCode, out var lobby) || lobby.State != GameState.Betting)
            {
                await Clients.Caller.SendAsync("BetRejected", "Betting is currently closed.");
                return;
            }

            var httpContext = Context.GetHttpContext();
            var playerId = httpContext?.Items["PlayerId"]?.ToString();

            if (string.IsNullOrEmpty(playerId))
            {
                await Clients.Caller.SendAsync("BetRejected", "Player not identified");
                return;
            }

            var player = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null) return;

            if (incomingBet.Amount <= 0)
            {
                await Clients.Caller.SendAsync("BetRejected", "Invalid bet amount");
                return;
            }

            // Check balance from wallet (this is safe, it's in the hub scope)
            if (!await _walletService.HasSufficientBalanceAsync(playerId, incomingBet.Amount))
            {
                await Clients.Caller.SendAsync("BetRejected", "Insufficient balance");
                return;
            }

            // Deduct from wallet (this is safe, it's in the hub scope)
            await _walletService.DeductBalanceAsync(playerId, incomingBet.Amount, "Roulette", $"Bet: {incomingBet.BetType} - {incomingBet.BetValue}");

            // Update local balance cache
            var newBalance = await _walletService.GetBalanceAsync(playerId);
            lobby.Balances[playerId] = newBalance;

            incomingBet.PlayerName = player.Name;
            incomingBet.PlayerId = playerId; // Store player ID

            lock (lobby.CurrentBets)
            {
                lobby.CurrentBets.Add(incomingBet);
            }

            await Clients.Caller.SendAsync("BalanceUpdated", newBalance);
        }

        public async Task StartGame(string lobbyCode)
        {
            if (Lobbies.TryGetValue(lobbyCode, out var lobby))
            {
                if (Context.ConnectionId == lobby.CreatorConnectionId)
                {
                    lobby.IsGameStarted = true;
                    await Clients.Group(lobbyCode).SendAsync("GameStarted", lobbyCode);
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Find the lobby this player was in
            var lobby = Lobbies.Values.FirstOrDefault(l => l.Players.Any(p => p.ConnectionId == Context.ConnectionId));

            if (lobby != null)
            {
                // Find the player
                var player = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    // Remove the player from the list
                    lobby.Players.Remove(player);

                    // Notify everyone in the group that the player list has changed
                    var playerNames = lobby.Players.Select(p => p.Name).ToList();
                    await Clients.Group(lobby.LobbyCode).SendAsync("UpdatePlayerList", playerNames, lobby.CreatorConnectionId);

                    // Send a specific disconnected message
                    await Clients.Group(lobby.LobbyCode).SendAsync("PlayerDisconnected", player.Name);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // --- THIS METHOD HAS BEEN MOVED TO LobbyInfo ---
        // public decimal CalculatePayout(BetInfo bet, int result) { ... }
    }

    public class LobbyInfo
    {
        public string LobbyCode { get; }
        public List<PlayerInfo> Players { get; set; } = new();
        public ConcurrentDictionary<string, decimal> Balances { get; set; } = new(); // Key: PlayerId
        public List<int> History { get; set; } = new();
        public List<BetInfo> CurrentBets { get; set; } = new();
        public string CreatorConnectionId { get; set; }
        public bool IsGameStarted { get; set; } = false;
        public GameState State { get; private set; } = GameState.Betting;
        public int Countdown { get; private set; }

        private readonly Timer _timer;
        private readonly IHubContext<RouletteHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory; // <-- CHANGED

        // --- MOVED THESE ARRAYS HERE ---
        private static readonly int[] RedNumbers = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        private static readonly int[] Column1 = { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34 };
        private static readonly int[] Column2 = { 2, 5, 8, 11, 14, 17, 20, 23, 26, 29, 32, 35 };
        private static readonly int[] Column3 = { 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36 };

        public LobbyInfo(string lobbyCode, IHubContext<RouletteHub> hubContext, IServiceScopeFactory scopeFactory) // <-- CHANGED
        {
            LobbyCode = lobbyCode;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory; // <-- CHANGED
            _timer = new Timer(GameTick, null, 1000, 1000);
            Countdown = 30;
        }

        private async void GameTick(object state)
        {
            Countdown--;

            if (State == GameState.Betting && Countdown > 0)
            {
                await _hubContext.Clients.Group(LobbyCode).SendAsync("UpdateTimer", Countdown);
            }

            if (Countdown > 0) return;

            switch (State)
            {
                case GameState.Betting:
                    State = GameState.Spinning;
                    Countdown = 8;
                    await _hubContext.Clients.Group(LobbyCode).SendAsync("StartSpin");
                    await ResolveSpin();
                    break;

                case GameState.Spinning:
                    State = GameState.Result;
                    Countdown = 3;
                    break;

                case GameState.Result:
                    State = GameState.Betting;
                    Countdown = 30;
                    await _hubContext.Clients.Group(LobbyCode).SendAsync("StartBetting", Countdown);
                    break;
            }
        }

        private async Task ResolveSpin()
        {
            int result = new Random().Next(0, 37);

            var playerWinnings = new Dictionary<string, decimal>(); // Key: PlayerId

            lock (CurrentBets)
            {
                foreach (var bet in CurrentBets)
                {
                    decimal payout = CalculatePayout(bet, result); // <-- USE LOCAL METHOD
                    if (payout > 0)
                    {
                        playerWinnings.TryAdd(bet.PlayerId, 0m);
                        playerWinnings[bet.PlayerId] += payout;
                    }
                }
                CurrentBets.Clear();
            }

            // ============ THIS IS THE FIX ============
            // Create a new scope to safely use scoped services
            using (var scope = _scopeFactory.CreateScope())
            {
                var walletService = scope.ServiceProvider.GetRequiredService<IWalletService>();

                // Update wallet balances
                foreach (var win in playerWinnings)
                {
                    await walletService.AddBalanceAsync(win.Key, win.Value, "Roulette", $"Won ₹{win.Value} on number {result}");
                    var newBalance = await walletService.GetBalanceAsync(win.Key);
                    Balances[win.Key] = newBalance;
                }
            }
            // =========================================

            History.Insert(0, result);
            if (History.Count > 10) History.RemoveAt(10);

            var leaderboard = Players
                .Select(p => new { Name = p.Name, Balance = Balances.GetValueOrDefault(p.PlayerId, 0) })
                .OrderByDescending(x => x.Balance)
                .ToList();

            await _hubContext.Clients.Group(LobbyCode).SendAsync("RoundResult", result, History, leaderboard);
        }

        // --- THIS METHOD WAS MOVED FROM THE HUB CLASS ---
        private decimal CalculatePayout(BetInfo bet, int result)
        {
            decimal odds = bet.BetType switch { "Single" => 36m, "Red" or "Black" or "Odd" or "Even" or "Low" or "High" => 2m, "Dozen" or "Column" => 3m, _ => 0m };
            bool win = false;
            if (bet.BetType == "Single") { if (int.TryParse(bet.BetValue, out int num)) { win = (num == result); } }
            else if (bet.BetType == "Red") { win = RedNumbers.Contains(result); }
            else if (bet.BetType == "Black") { win = !RedNumbers.Contains(result) && result != 0; }
            else if (bet.BetType == "Odd") { win = (result % 2 == 1 && result != 0); }
            else if (bet.BetType == "Even") { win = (result % 2 == 0 && result != 0); }
            else if (bet.BetType == "Low") { win = (result >= 1 && result <= 18); }
            else if (bet.BetType == "High") { win = (result >= 19 && result <= 36); }
            else if (bet.BetType == "Dozen") { if (int.TryParse(bet.BetValue, out int dz)) { win = (dz == 1 && result >= 1 && result <= 12) || (dz == 2 && result >= 13 && result <= 24) || (dz == 3 && result >= 25 && result <= 36); } }
            else if (bet.BetType == "Column") { if (int.TryParse(bet.BetValue, out int col)) { win = (col == 1 && Column1.Contains(result)) || (col == 2 && Column2.Contains(result)) || (col == 3 && Column3.Contains(result)); } }
            return win ? bet.Amount * odds : 0m;
        }
    }

    public class PlayerInfo
    {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
        public string PlayerId { get; set; } // Cookie-based player ID
    }
}