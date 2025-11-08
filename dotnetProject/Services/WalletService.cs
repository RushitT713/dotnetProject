using dotnetProject.Data;
using dotnetProject.Models;
using Microsoft.EntityFrameworkCore;

namespace dotnetProject.Services
{
    public interface IWalletService
    {
        Task<Player> GetOrCreatePlayerAsync(string playerId, string? displayName = null);
        Task<decimal> GetBalanceAsync(string playerId);
        Task<bool> DeductBalanceAsync(string playerId, decimal amount, string gameType, string? description = null);
        Task<bool> AddBalanceAsync(string playerId, decimal amount, string gameType, string? description = null);
        Task<bool> HasSufficientBalanceAsync(string playerId, decimal amount);
        Task<List<Transaction>> GetTransactionHistoryAsync(string playerId, int count = 20);
    }

    public class WalletService : IWalletService
    {
        private readonly CasinoDbContext _context;
        private readonly ILogger<WalletService> _logger;

        public WalletService(CasinoDbContext context, ILogger<WalletService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Player> GetOrCreatePlayerAsync(string playerId, string? displayName = null)
        {
            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerId == playerId);

            if (player == null)
            {
                player = new Player
                {
                    PlayerId = playerId,
                    DisplayName = displayName,
                    Balance = 5000m, // Starting balance
                    CreatedAt = DateTime.UtcNow,
                    LastActive = DateTime.UtcNow
                };

                _context.Players.Add(player);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Created new player: {playerId} with starting balance ₹5000");
            }
            else
            {
                // Update last active time
                player.LastActive = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(player.DisplayName))
                {
                    player.DisplayName = displayName;
                }
                await _context.SaveChangesAsync();
            }

            return player;
        }

        public async Task<decimal> GetBalanceAsync(string playerId)
        {
            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerId == playerId);

            return player?.Balance ?? 0m;
        }

        public async Task<bool> DeductBalanceAsync(string playerId, decimal amount, string gameType, string? description = null)
        {
            if (amount <= 0)
            {
                _logger.LogWarning($"Invalid deduction amount: {amount}");
                return false;
            }

            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerId == playerId);

            if (player == null || player.Balance < amount)
            {
                _logger.LogWarning($"Insufficient balance for player {playerId}. Required: {amount}, Available: {player?.Balance ?? 0}");
                return false;
            }

            var amountBefore = player.Balance;
            player.Balance -= amount;
            player.LastActive = DateTime.UtcNow;

            // Record transaction
            var transaction = new Transaction
            {
                PlayerId = player.Id,
                GameType = gameType,
                AmountBefore = amountBefore,
                AmountChange = -amount,
                AmountAfter = player.Balance,
                Description = description ?? $"Bet placed in {gameType}",
                CreatedAt = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Deducted ₹{amount} from {playerId}. New balance: ₹{player.Balance}");
            return true;
        }

        public async Task<bool> AddBalanceAsync(string playerId, decimal amount, string gameType, string? description = null)
        {
            if (amount <= 0)
            {
                _logger.LogWarning($"Invalid addition amount: {amount}");
                return false;
            }

            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerId == playerId);

            if (player == null)
            {
                _logger.LogWarning($"Player {playerId} not found");
                return false;
            }

            var amountBefore = player.Balance;
            player.Balance += amount;
            player.LastActive = DateTime.UtcNow;

            // Record transaction
            var transaction = new Transaction
            {
                PlayerId = player.Id,
                GameType = gameType,
                AmountBefore = amountBefore,
                AmountChange = amount,
                AmountAfter = player.Balance,
                Description = description ?? $"Win in {gameType}",
                CreatedAt = DateTime.UtcNow
            };

            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Added ₹{amount} to {playerId}. New balance: ₹{player.Balance}");
            return true;
        }

        public async Task<bool> HasSufficientBalanceAsync(string playerId, decimal amount)
        {
            var balance = await GetBalanceAsync(playerId);
            return balance >= amount;
        }

        public async Task<List<Transaction>> GetTransactionHistoryAsync(string playerId, int count = 20)
        {
            var player = await _context.Players
                .FirstOrDefaultAsync(p => p.PlayerId == playerId);

            if (player == null) return new List<Transaction>();

            return await _context.Transactions
                .Where(t => t.PlayerId == player.Id)
                .OrderByDescending(t => t.CreatedAt)
                .Take(count)
                .ToListAsync();
        }
    }
}