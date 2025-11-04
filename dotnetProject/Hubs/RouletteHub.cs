using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dotnetProject.Models;

namespace dotnetProject.Hubs
{
    // Step 1: Define the possible states of a game round.
    public enum GameState { Betting, Spinning, Result }

    public class RouletteHub : Hub
    {
        // The Lobbies dictionary is now static to persist across hub instances.
        private static readonly ConcurrentDictionary<string, LobbyInfo> Lobbies = new();

        // (Your number definitions remain the same)
        private static readonly int[] RedNumbers = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        private static readonly int[] Column1 = { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34 };
        private static readonly int[] Column2 = { 2, 5, 8, 11, 14, 17, 20, 23, 26, 29, 32, 35 };
        private static readonly int[] Column3 = { 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36 };

        // Step 2: Overhaul the JoinLobby method to handle reconnections.
        // In RouletteHub.cs

        // In RouletteHub.cs

        public async Task JoinLobby(string lobbyCode, string playerName)
        {
            var lobby = Lobbies.GetOrAdd(lobbyCode, _ => {
                var newLobby = new LobbyInfo(lobbyCode,
                    (IHubContext<RouletteHub>)Context.GetHttpContext().RequestServices.GetService(typeof(IHubContext<RouletteHub>))
                );
                newLobby.CreatorConnectionId = Context.ConnectionId;
                return newLobby;
            });

            var player = lobby.Players.FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (player != null) // Player is reconnecting
            {
                player.ConnectionId = Context.ConnectionId;
            }
            else // This is a new player
            {
                player = new PlayerInfo { ConnectionId = Context.ConnectionId, Name = playerName };
                lobby.Players.Add(player);
                lobby.Balances[playerName] = 5000m;
            }

            // This is the unified notification message
            string notificationMessage = $"{playerName} has connected.";

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);
            await Clients.OthersInGroup(lobbyCode).SendAsync("PlayerJoined", notificationMessage);

            // ... The rest of the method remains the same ...
            await Clients.Caller.SendAsync("SetConnectionId", Context.ConnectionId);
            var playerNames = lobby.Players.Select(p => p.Name).ToList();
            await Clients.Group(lobbyCode).SendAsync("UpdatePlayerList", playerNames, lobby.CreatorConnectionId);

            if (lobby.IsGameStarted)
            {
                await Clients.Caller.SendAsync("GameStarted", lobbyCode);
            }

            var leaderboard = lobby.Players
                .Select(p => new { Name = p.Name, Balance = lobby.Balances.GetValueOrDefault(p.Name, 0) })
                .OrderByDescending(x => x.Balance)
                .ToList();
            await Clients.Caller.SendAsync("InitState", lobby.History, leaderboard, lobby.State, lobby.Countdown);
        }
        // Step 3: Update GetLobbyState to use PlayerName for balance lookups.
        //public async Task GetLobbyState(string lobbyCode)
        //{
        //    if (Lobbies.TryGetValue(lobbyCode, out var lobby))
        //    {
        //        var leaderboard = lobby.Players
        //            .Select(p => new { Name = p.Name, Balance = lobby.Balances.GetValueOrDefault(p.Name, 0) })
        //            .OrderByDescending(x => x.Balance)
        //            .ToList();

        //        // Also send the current timer and state to the joining player.
        //        await Clients.Caller.SendAsync("InitState", lobby.History, leaderboard, lobby.State, lobby.Countdown);
        //    }
        //}

        // Step 4: Update PlaceBet to be server-authoritative.
        public async Task PlaceBet(string lobbyCode, BetInfo incomingBet)
        {
            if (!Lobbies.TryGetValue(lobbyCode, out var lobby) || lobby.State != GameState.Betting)
            {
                await Clients.Caller.SendAsync("BetRejected", "Betting is currently closed.");
                return;
            }

            // Find player by their connection ID to get their stable name.
            var playerName = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId)?.Name;
            if (playerName == null) return; // Player not found

            var playerBalance = lobby.Balances[playerName];
            if (playerBalance < incomingBet.Amount)
            {
                await Clients.Caller.SendAsync("BetRejected", "Insufficient balance");
                return;
            }

            lobby.Balances[playerName] -= incomingBet.Amount;
            incomingBet.PlayerName = playerName; // Tag the bet with the player's name.

            lock (lobby.CurrentBets)
            {
                lobby.CurrentBets.Add(incomingBet);
            }

            await Clients.Caller.SendAsync("BalanceUpdated", lobby.Balances[playerName]);
        }

        // Note: We remove the client-callable SpinWheel method. The server loop now calls an internal version.
        // We also remove StartGame and UpdatePlayerList, as the loop handles this.
        public async Task StartGame(string lobbyCode)
        {
            if (Lobbies.TryGetValue(lobbyCode, out var lobby))
            {
                // Security Check: Only allow the creator to start the game
                if (Context.ConnectionId == lobby.CreatorConnectionId)
                {
                    lobby.IsGameStarted = true;
                    // Tell everyone in this lobby that the game is starting
                    await Clients.Group(lobbyCode).SendAsync("GameStarted", lobbyCode);
                }
            }
        }
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // We don't remove the player from the lobby on disconnect anymore.
            // This allows them to refresh and rejoin with their balance intact.
            // A more advanced system would add a timeout to remove inactive players.
            await base.OnDisconnectedAsync(exception);
        }

        // (The CalculatePayout method remains the same as your original)
        public decimal CalculatePayout(BetInfo bet, int result)
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

    // Step 5: The LobbyInfo class now contains the entire server-side game loop.
    public class LobbyInfo
    {
        public string LobbyCode { get; }
        public List<PlayerInfo> Players { get; set; } = new();
        public ConcurrentDictionary<string, decimal> Balances { get; set; } = new();
        public List<int> History { get; set; } = new();
        public List<BetInfo> CurrentBets { get; set; } = new();
        public string CreatorConnectionId { get; set; }
        public bool IsGameStarted { get; set; } = false;
        // Game loop properties
        public GameState State { get; private set; } = GameState.Betting;
        public int Countdown { get; private set; }
        private readonly Timer _timer;
        private readonly IHubContext<RouletteHub> _hubContext;
        private readonly RouletteHub _rouletteHubInstance; // To access CalculatePayout

        public LobbyInfo(string lobbyCode, IHubContext<RouletteHub> hubContext)
        {
            LobbyCode = lobbyCode;
            _hubContext = hubContext;
            _rouletteHubInstance = new RouletteHub(); // Create an instance to use its methods
            _timer = new Timer(GameTick, null, 1000, 1000); // Start the timer to tick every second
            Countdown = 30; // Start with 30 seconds of betting
        }

        private async void GameTick(object state)
        {
            Countdown--;

            // Send timer updates only during the betting phase
            if (State == GameState.Betting && Countdown > 0)
            {
                await _hubContext.Clients.Group(LobbyCode).SendAsync("UpdateTimer", Countdown);
            }

            // If the countdown is still running, just wait for the next tick.
            if (Countdown > 0)
            {
                return;
            }

            // When countdown reaches 0, transition to the next state.
            switch (State)
            {
                case GameState.Betting:
                    // Betting is over. Start the spin.
                    State = GameState.Spinning;
                    Countdown = 8; // 7s for client spin animation + 1s buffer.
                    await _hubContext.Clients.Group(LobbyCode).SendAsync("StartSpin");
                    await ResolveSpin(); // Calculate the result now, but the state remains "Spinning".
                    break;

                case GameState.Spinning:
                    // The spin animation has finished. Show the result.
                    State = GameState.Result;
                    Countdown = 3; // 3s for players to see the result pop-up.
                    break;

                case GameState.Result:
                    // The result has been shown. Start the next betting round.
                    State = GameState.Betting;
                    Countdown = 30; // 30s for betting.
                    await _hubContext.Clients.Group(LobbyCode).SendAsync("StartBetting", Countdown);
                    break;
            }
        }

        private async Task ResolveSpin()
        {
            int result = new Random().Next(0, 37);

            var playerWinnings = new Dictionary<string, decimal>();
            lock (CurrentBets)
            {
                foreach (var bet in CurrentBets)
                {
                    decimal payout = _rouletteHubInstance.CalculatePayout(bet, result);
                    if (payout > 0)
                    {
                        playerWinnings.TryAdd(bet.PlayerName, 0m);
                        playerWinnings[bet.PlayerName] += payout;
                    }
                }
                CurrentBets.Clear();
            }

            foreach (var win in playerWinnings)
            {
                Balances[win.Key] += win.Value;
            }

            History.Insert(0, result);
            if (History.Count > 10) History.RemoveAt(10);

            var leaderboard = Players
                .Select(p => new { Name = p.Name, Balance = Balances.GetValueOrDefault(p.Name, 0) })
                .OrderByDescending(x => x.Balance)
                .ToList();

            await _hubContext.Clients.Group(LobbyCode).SendAsync("RoundResult", result, History, leaderboard);
        }
    }

    // Step 6: Add PlayerName to BetInfo so bets are tied to players, not connections.
   
    // (PlayerInfo class remains the same)
    public class PlayerInfo
    {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
    }
}