using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Leagues
{
    public partial class Leagues
    {
        [JsonProperty("api")]
        public Api Api { get; set; }
    }

    public partial class Api
    {
        [JsonProperty("leagues")]
        public List<League> Leagues { get; set; }
    }

    public partial class League
    {
        [JsonProperty("season")]
        public string Season { get; set; }
    }
}

namespace Fixtures
{
    public partial class Fixtures
    {
        [JsonProperty("api")]
        public Api Api { get; set; }
    }

    public partial class Api
    {
        [JsonProperty("results")]
        public int Results { get; set; }

        [JsonProperty("fixtures")]
        public List<Fixture> Fixtures { get; set; }
    }

    public partial class Fixture
    {
        [JsonProperty("fixture_id")]
        public string FixtureId { get; set; }

        [JsonProperty("league")]
        public League League { get; set; }

        [JsonProperty("event_date")]
        public DateTimeOffset EventDate { get; set; }

        [JsonProperty("round")]
        public string Round { get; set; }

        [JsonProperty("statusShort")]
        public string StatusShort { get; set; }

        [JsonProperty("homeTeam")]
        public Team HomeTeam { get; set; }

        [JsonProperty("awayTeam")]
        public Team AwayTeam { get; set; }

        [JsonProperty("goalsHomeTeam")]
        public int? GoalsHomeTeam { get; set; }

        [JsonProperty("goalsAwayTeam")]
        public int? GoalsAwayTeam { get; set; }
    }

    public partial class Team
    {
        [JsonProperty("team_id")]
        public int TeamId { get; set; }

        [JsonProperty("team_name")]
        public string TeamName { get; set; }

        [JsonProperty("logo")]
        public Uri Logo { get; set; }
    }

    public partial class League
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("logo")]
        public Uri Logo { get; set; }

        [JsonProperty("flag")]
        public Uri Flag { get; set; }
    }
}

namespace Odds
{
    public partial class Odds
    {
        [JsonProperty("api")]
        public Api Api { get; set; }
    }

    public partial class Api
    {
        [JsonProperty("results")]
        public int Results { get; set; }

        [JsonProperty("paging")]
        public Paging Paging { get; set; }

        [JsonProperty("odds")]
        public List<Odd> Odds { get; set; }
    }

    public partial class Odd
    {
        [JsonProperty("fixture")]
        public Fixture Fixture { get; set; }

        [JsonProperty("bookmakers")]
        public List<Bookmaker> Bookmakers { get; set; }
    }

    public partial class Bookmaker
    {
        [JsonProperty("bookmaker_id")]
        public string BookmakerId { get; set; }

        [JsonProperty("bookmaker_name")]
        public string BookmakerName { get; set; }

        [JsonProperty("bets")]
        public List<Bet> Bets { get; set; }
    }

    public partial class Bet
    {
        [JsonProperty("label_id")]
        public int LabelId { get; set; }

        [JsonProperty("label_name")]
        public string LabelName { get; set; }

        [JsonProperty("values")]
        public List<ValueElement> Values { get; set; }
    }

    public partial class ValueElement
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("odd")]
        public string Odd { get; set; }
    }

    public partial class Fixture
    {
        [JsonProperty("fixture_id")]
        public string FixtureId { get; set; }

        [JsonProperty("updateAt")]
        public long UpdateAt { get; set; }
    }

    public partial class Paging
    {
        [JsonProperty("current")]
        public int Current { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }
}

namespace Teams
{
    public partial class Teams
    {
        [JsonProperty("api")]
        public Api Api { get; set; }
    }

    public partial class Api
    {
        [JsonProperty("teams")]
        public List<Team> Teams { get; set; }
    }

    public partial class Team
    {
        [JsonProperty("team_id")]
        public string TeamId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("logo")]
        public string LogoUri { get; set; }
    }
}

namespace Standings
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public partial class Standings
    {
        [JsonProperty("api")]
        public Api Api { get; set; }
    }

    public partial class Api
    {
        [JsonProperty("standings")]
        public List<List<Standing>> Standings { get; set; }
    }

    public partial class Standing
    {
        [JsonProperty("team_id")]
        public string TeamId { get; set; }

        [JsonProperty("rank")]
        public int Position { get; set; }

        [JsonProperty("points")]
        public int Points { get; set; }

        [JsonProperty("forme")]
        public string Forme { get; set; }

        [JsonProperty("all")]
        public All All { get; set; }
    }

    public partial class All
    {
        [JsonProperty("matchsPlayed")]
        public int MatchesPlayed { get; set; }

        [JsonProperty("win")]
        public int Win { get; set; }

        [JsonProperty("draw")]
        public int Draw { get; set; }

        [JsonProperty("lose")]
        public int Lose { get; set; }

        [JsonProperty("goalsFor")]
        public int GoalsFor { get; set; }

        [JsonProperty("goalsAgainst")]
        public int GoalsAgainst { get; set; }
    }
}
