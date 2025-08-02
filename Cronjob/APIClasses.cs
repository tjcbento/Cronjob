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
    public class FixturesApiResponse
    {
        [JsonProperty("response")]
        public List<Fixture> Response { get; set; }

        [JsonProperty("paging")]
        public Paging Paging { get; set; }
    }

    public class Fixture
    {
        [JsonProperty("fixture")]
        public FixtureInfo FixtureInfo { get; set; }

        [JsonProperty("teams")]
        public Teams Teams { get; set; }

        [JsonProperty("league")]
        public League League { get; set; }

        [JsonProperty("goals")]
        public Goals Goals { get; set; }
    }

    public class FixtureInfo
    {
        [JsonProperty("id")]
        public string FixtureId { get; set; }

        [JsonProperty("date")]
        public DateTime EventDate { get; set; }

        [JsonProperty("status")]
        public Status Status { get; set; }
    }

    public class Goals
    {
        [JsonProperty("home")]
        public int? Home { get; set; }

        [JsonProperty("away")]
        public int? Away { get; set; }
    }

    public class Status
    {
        [JsonProperty("short")]
        public string Short { get; set; }
    }

    public class Teams
    {
        [JsonProperty("home")]
        public Team HomeTeam { get; set; }

        [JsonProperty("away")]
        public Team AwayTeam { get; set; }
    }

    public class Team
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class League
    {
        [JsonProperty("round")]
        public string Round { get; set; }
    }

    public class Paging
    {
        [JsonProperty("current")]
        public int Current { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }
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

namespace Odds
{
    public class OddsApiResponse
    {
        [JsonProperty("response")]
        public List<Odd> Odds { get; set; }

        [JsonProperty("paging")]
        public Paging Paging { get; set; }
    }

    public class Odd
    {
        [JsonProperty("fixture")]
        public Fixture Fixture { get; set; }

        [JsonProperty("bookmakers")]
        public List<Bookmaker> Bookmakers { get; set; }
    }

    public class Fixture
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }

    public class Bookmaker
    {
        [JsonProperty("bets")]
        public List<Bet> Bets { get; set; }
    }

    public class Bet
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("values")]
        public List<BetValue> Values { get; set; }
    }

    public class BetValue
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("odd")]
        public string Odd { get; set; }
    }

    public class Paging
    {
        [JsonProperty("current")]
        public int Current { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }
}

namespace Teams
{
    public class TeamsApiResponse
    {
        [JsonProperty("response")]
        public List<TeamWrapper> Response { get; set; }
    }

    public class TeamWrapper
    {
        [JsonProperty("team")]
        public Team Team { get; set; }
    }

    public class Team
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("logo")]
        public string Logo { get; set; }
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

namespace Videos
{
    public partial class Videos
    {
        [JsonProperty("contents")]
        public List<Content> Contents { get; set; }
    }

    public partial class Content
    {
        [JsonProperty("video")]
        public Video Video { get; set; }
    }

    public partial class Video
    {
        [JsonProperty("videoId")]
        public string VideoId { get; set; }
    }
}
