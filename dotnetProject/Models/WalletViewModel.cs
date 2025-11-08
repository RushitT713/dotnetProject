using System.Collections.Generic;

namespace dotnetProject.Models
{
    public class WalletViewModel
    {
        public decimal CurrentBalance { get; set; }
        public List<Transaction> History { get; set; } = new List<Transaction>();
    }
}