using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Odds;
using RestSharp;
using System.Linq;

namespace Cronjob
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[{0}]: Update script starting...", DateTime.Now.ToString());

            string connectionString = string.Format(
                "server={0};user={1};password={2};port={3};database={4}",
                new string[]
                {
                    Environment.GetEnvironmentVariable("DB_URL"),
                    Environment.GetEnvironmentVariable("DB_USER"),
                    Environment.GetEnvironmentVariable("DB_PASSWORD"),
                    Environment.GetEnvironmentVariable("DB_PORT"),
                    Environment.GetEnvironmentVariable("DB_DATABASE")
                });

            _logger.LogInformation("[{0}]: Connecting to MySQL...", DateTime.Now.ToString());
            MySqlConnection connection = new MySqlConnection(connectionString);
            connection.Open();
            _logger.LogInformation("[{0}]: Connection to MySQL successful!", DateTime.Now.ToString());

            Dictionary<string, bool> queriedFixtures = new Dictionary<string, bool>();
            string apiUrl = "https://api-football-v1.p.rapidapi.com".TrimEnd('/');
            string leagueId = "2826";
            List<string> bookmakerPriority = "6;16;10".Split(';').ToList<string>();
            string xRapidApiKey = "71c224cf07mshba741ff2d8909bap1cf196jsn064c478360bd";
            string xRapidApiHost = "api-football-v1.p.rapidapi.com";

            var fixtures = GetFixtures(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
            var odds = GetOdds(apiUrl, leagueId, bookmakerPriority, xRapidApiKey, xRapidApiHost);

            foreach (var match in fixtures)
            {
                if (queriedFixtures.TryGetValue(match.FixtureId, out bool queriedMatchUpdateNeeded))
                {
                    if (!queriedMatchUpdateNeeded)
                    {
                        _logger.LogInformation(
                            "[{0}]: Skipping match update for FixtureId='{1}'...",
                            new object[]
                            {
                                DateTime.Now.ToString(),
                                match.FixtureId
                            });

                        continue;
                    }

                    char? result = null;
                    if (match.StatusShort == "FT")
                    {
                        result = ProcessResult(match.GoalsHomeTeam, match.GoalsAwayTeam);
                    }

                    double? oddHome = null;
                    double? oddDraw = null;
                    double? oddAway = null;
                    if (match.StatusShort == "NS" && odds.TryGetValue(match.FixtureId, out SimpleOdd odd))
                    {
                        oddHome = odd.OddHome;
                        oddDraw = odd.OddDraw;
                        oddAway = odd.OddAway;
                    }

                    // UPDATE
                    MySqlCommand command = new MySqlCommand("SPName", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    command.Parameters.Add(new MySqlParameter("VarName01", match.FixtureId));
                    command.Parameters.Add(new MySqlParameter("VarName02", match.EventDate));
                    command.Parameters.Add(new MySqlParameter("VarName03", match.Round.Split('-')[1][1..]));
                    command.Parameters.Add(new MySqlParameter("VarName04", match.StatusShort));
                    command.Parameters.Add(new MySqlParameter("VarName05", match.HomeTeam.TeamName));
                    command.Parameters.Add(new MySqlParameter("VarName06", match.AwayTeam.TeamName));
                    command.Parameters.Add(new MySqlParameter("VarName07", match.GoalsHomeTeam));
                    command.Parameters.Add(new MySqlParameter("VarName08", match.GoalsAwayTeam));
                    command.Parameters.Add(new MySqlParameter("VarName09", result));
                    command.Parameters.Add(new MySqlParameter("VarName10", oddHome));
                    command.Parameters.Add(new MySqlParameter("VarName11", oddDraw));
                    command.Parameters.Add(new MySqlParameter("VarName12", oddAway));

                    command.Connection.Open();
                    command.ExecuteNonQuery();
                    command.Connection.Close();

                    _logger.LogInformation(
                        "[{0}]: Updated match data for FixtureId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            match.FixtureId
                        });
                }
                else
                {
                    // INSERT
                    _logger.LogInformation(
                      "[{0}]: Inserted match data for FixtureId='{1}'",
                      new object[]
                      {
                            DateTime.Now.ToString(),
                            match.FixtureId
                      });
                }
            }

            _logger.LogInformation("[{0}]: Closing connection to MySQL...", DateTime.Now.ToString());
            connection.Close();
            _logger.LogInformation("[{0}]: Connection to MySQL successful closed!", DateTime.Now.ToString());

            return Task.CompletedTask;
        }

        private char ProcessResult(int? goalsHomeTeam, int? goalsAwayTeam)
        {
            if (goalsHomeTeam > goalsAwayTeam)
            {
                return 'W';
            }
            else if (goalsHomeTeam < goalsAwayTeam)
            {
                return 'L';
            }
            else
            {
                return 'D';
            }
        }

        private static Dictionary<string, SimpleOdd> GetOdds(string apiUrl, string leagueId, List<string> bookmakerPriority, string xRapidApiKey, string xRapidApiHost)
        {
            int page = 1;
            var oddsUrl = new RestClient(apiUrl + "/v2/odds/league/" + leagueId + "/label/1");
            var oddsRequest = new RestRequest(Method.GET);
            oddsRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            oddsRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);

            List<Odd> odds = new List<Odd>();

            while (true)
            {
                oddsRequest.AddOrUpdateParameter("page", page.ToString());
                IRestResponse oddsResponse = oddsUrl.Execute(oddsRequest);

                var parsedOdds = JsonConvert.DeserializeObject<Odds.Odds>(oddsResponse.Content);
                odds.AddRange(parsedOdds.Api.Odds);

                if (parsedOdds.Api.Paging.Current == parsedOdds.Api.Paging.Total)
                {
                    break;
                }

                page++;
            }

            return odds.ToDictionary(
                odd => odd.Fixture.FixtureId,
                odd =>
                {
                    Bookmaker bookmaker = null;
                    foreach (var bookmakerId in bookmakerPriority)
                    {
                        bookmaker = odd.Bookmakers.FirstOrDefault(auxBookmaker => auxBookmaker.BookmakerId == bookmakerId);

                        if (bookmaker != null)
                        {
                            break;
                        }
                    }

                    if (bookmaker == null)
                    {
                        bookmaker = odd.Bookmakers.FirstOrDefault();
                    }

                    return new SimpleOdd(bookmaker);
                });
        }

        private static List<Fixtures.Fixture> GetFixtures(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var fixturesUrl = new RestClient(apiUrl + "/v2/fixtures/league/" + leagueId);
            var fixturesRequest = new RestRequest(Method.GET);
            fixturesRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            fixturesRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse fixturesResponse = fixturesUrl.Execute(fixturesRequest);

            var parsedFixtures = JsonConvert.DeserializeObject<Fixtures.Fixtures>(fixturesResponse.Content);

            return parsedFixtures.Api.Fixtures;
        }
    }
}
