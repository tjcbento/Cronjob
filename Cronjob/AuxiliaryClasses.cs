using System;
using System.Linq;
using Odds;

namespace SimpleOdd
{
    public class SimpleOdd
    {
        public SimpleOdd()
        {
        }

        public SimpleOdd(Bookmaker bookmaker)
        {
            ExtractFromBookmaker(bookmaker);
        }

        public double? OddHome { get; set; }

        public double? OddDraw { get; set; }

        public double? OddAway { get; set; }

        public void ExtractFromBookmaker(Bookmaker bookmaker)
        {
            var bets = bookmaker.Bets.FirstOrDefault();

            foreach (var bet in bets.Values)
            {
                if (bet.Value == "Home")
                {
                    OddHome = Convert.ToDouble(bet.Odd);
                }
                else if (bet.Value == "Draw")
                {
                    OddDraw = Convert.ToDouble(bet.Odd);
                }
                else if (bet.Value == "Away")
                {
                    OddAway = Convert.ToDouble(bet.Odd);
                }
            }
        }
    }
}

namespace QueriedMatch
{
    public class QueriedMatch
    {
        public QueriedMatch()
        {
        }

        public bool RequiresUpdate { get; set; }

        public string Result { get; set; }

        public int? Matchday { get; set; }

        public double? Multiplier { get; set; }

        public SimpleOdd.SimpleOdd SimpleOdd { get; set; }
    }
}