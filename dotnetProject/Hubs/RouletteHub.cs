using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dotnetProject.Models;

namespace dotnetProject.Hubs
{
    public class RouletteHub : Hub
    {
        private static ConcurrentDictionary<string, LobbyInfo> Lobbies = new();

        private static readonly int[] RedNumbers = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        private static readonly int[] Column1 = { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28, 31, 34 };
        private static readonly int[] Column2 = { 2, 5, 8, 11, 14, 17, 20, 23, 26, 29, 32, 35 };
        private static readonly int[] Column3 = { 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36 };

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("SetConnectionId", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public async Task JoinLobby(string lobbyCode, string playerName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);

            var lobby = Lobbies.GetOrAdd(lobbyCode, _ => new LobbyInfo
            {
                CreatorConnectionId = Context.ConnectionId,
                Players = new List<PlayerInfo>(),
                Balances = new ConcurrentDictionary<string, decimal>(),
                History = new List<int>(),
                CurrentBets = new List<BetInfo>()
            });

            if (!lobby.Players.Any(p => p.ConnectionId == Context.ConnectionId))
            {
                lobby.Players.Add(new PlayerInfo
                {
                    ConnectionId = Context.ConnectionId,
                    Name = playerName
                });
                lobby.Balances[Context.ConnectionId] = 5000m;
            }

            var initLeaderboard = lobby.Players
                .Select(p => new { Name = p.Name, Balance = lobby.Balances[p.ConnectionId] })
                .OrderByDescending(x => x.Balance)
                .ToList();

            await Clients.Caller.SendAsync("InitState", lobby.History, initLeaderboard);
            await UpdatePlayerList(lobbyCode);
        }

        public async Task GetLobbyState(string lobbyCode)
        {
            if (Lobbies.TryGetValue(lobbyCode, out var lobby))
            {
                var leaderboard = lobby.Players
                    .Select(p => new { Name = p.Name, Balance = lobby.Balances[p.ConnectionId] })
                    .OrderByDescending(x => x.Balance)
                    .ToList();

                await Clients.Caller.SendAsync("InitState", lobby.History, leaderboard);
            }
        }

        public async Task StartGame(string lobbyCode)
        {
            await Clients.Group(lobbyCode).SendAsync("GameStarted", lobbyCode);
        }

        public async Task PlaceBet(string lobbyCode, BetInfo incomingBet)
        {
            var lobby = Lobbies[lobbyCode];
            var playerBalance = lobby.Balances[Context.ConnectionId];

            if (playerBalance < incomingBet.Amount)
            {
                await Clients.Caller.SendAsync("BetRejected", "Insufficient balance");
                return;
            }

            lobby.Balances[Context.ConnectionId] -= incomingBet.Amount;

            var bet = new BetInfo
            {
                ConnectionId = Context.ConnectionId,
                BetType = incomingBet.BetType,
                BetValue = incomingBet.BetValue,
                Amount = incomingBet.Amount
            };

            lock (lobby.CurrentBets)
            {
                lobby.CurrentBets.Add(bet);
            }

            await Clients.Caller.SendAsync("BalanceUpdated", lobby.Balances[Context.ConnectionId]);
        }

        public async Task SpinWheel(string lobbyCode)
        {
            var lobby = Lobbies[lobbyCode];
            int result = new Random().Next(0, 37); // 0–36 for European roulette

            lock (lobby.CurrentBets)
            {
                foreach (var bet in lobby.CurrentBets)
                {
                    var payout = CalculatePayout(bet, result);
                    if (payout > 0)
                    {
                        lobby.Balances[bet.ConnectionId] += payout;
                    }
                }
                lobby.CurrentBets.Clear();
            }

            lobby.History.Insert(0, result);
            if (lobby.History.Count > 10) lobby.History.RemoveAt(10);

            var leaderboard = lobby.Players
                .Select(p => new { Name = p.Name, Balance = lobby.Balances[p.ConnectionId] })
                .OrderByDescending(x => x.Balance)
                .ToList();

            await Clients.Group(lobbyCode).SendAsync("RoundResult", result, lobby.History, leaderboard);
        }

        private async Task UpdatePlayerList(string lobbyCode)
        {
            if (Lobbies.TryGetValue(lobbyCode, out var lobby))
            {
                var names = lobby.Players.Select(p => p.Name).ToList();
                await Clients.Group(lobbyCode)
                    .SendAsync("UpdatePlayerList", names, lobby.CreatorConnectionId);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            foreach (var kv in Lobbies)
            {
                var code = kv.Key;
                var lobby = kv.Value;
                var player = lobby.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    lobby.Players.Remove(player);
                    await UpdatePlayerList(code);
                    break;
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        private decimal CalculatePayout(BetInfo bet, int result)
        {
            decimal odds = bet.BetType switch
            {
                "Single" => 36m,
                "Red" or "Black" or "Odd" or "Even" or "Low" or "High" => 2m,
                "Dozen" or "Column" => 3m,
                _ => 0m
            };

            bool win = false;

            if (bet.BetType == "Single")
            {
                if (int.TryParse(bet.BetValue, out int num))
                {
                    win = (num == result);
                }
            }
            else if (bet.BetType == "Red")
            {
                win = RedNumbers.Contains(result);
            }
            else if (bet.BetType == "Black")
            {
                win = !RedNumbers.Contains(result) && result != 0;
            }
            else if (bet.BetType == "Odd")
            {
                win = (result % 2 == 1 && result != 0);
            }
            else if (bet.BetType == "Even")
            {
                win = (result % 2 == 0 && result != 0);
            }
            else if (bet.BetType == "Low")
            {
                win = (result >= 1 && result <= 18);
            }
            else if (bet.BetType == "High")
            {
                win = (result >= 19 && result <= 36);
            }
            else if (bet.BetType == "Dozen")
            {
                if (int.TryParse(bet.BetValue, out int dz))
                {
                    win = (dz == 1 && result >= 1 && result <= 12) ||
                          (dz == 2 && result >= 13 && result <= 24) ||
                          (dz == 3 && result >= 25 && result <= 36);
                }
            }
            else if (bet.BetType == "Column")
            {
                if (int.TryParse(bet.BetValue, out int col))
                {
                    win = (col == 1 && Column1.Contains(result)) ||
                          (col == 2 && Column2.Contains(result)) ||
                          (col == 3 && Column3.Contains(result));
                }
            }

            return win ? bet.Amount * odds : 0m;
        }
    }

    public class LobbyInfo
    {
        public string CreatorConnectionId { get; set; }
        public List<PlayerInfo> Players { get; set; }
        public ConcurrentDictionary<string, decimal> Balances { get; set; }
        public List<int> History { get; set; }
        public List<BetInfo> CurrentBets { get; set; }
    }

    public class PlayerInfo
    {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
    }
}