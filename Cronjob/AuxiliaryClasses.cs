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

        public double? OddHome { get; set; }

        public double? OddDraw { get; set; }

        public double? OddAway { get; set; }
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