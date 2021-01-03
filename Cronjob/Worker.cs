using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Odds;
using RestSharp;

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

            Dictionary<string, string> globalConstants = GetGlobalConstants(connection);
            List<string> queriedTeams = GetAvailableTeams(connection);
            Dictionary<string, QueriedMatch.QueriedMatch> queriedFixtures = GetAvailableFixtures(connection);
            List<string> unprocessedBets = GetUnprocessedBets(connection);

            string leagueId = globalConstants["LEAGUE_ID"];
            string apiUrl = globalConstants["API_URL"].TrimEnd('/');
            string preferedBookmaker = globalConstants["PREFERED_BOOKMAKER"];
            string xRapidApiKey = globalConstants["X_RAPID_API_KEY"];
            string xRapidApiHost = globalConstants["X_RAPID_API_HOST"];
            int numberMatchdayMultiplier = Convert.ToInt32(globalConstants["NUMBER_MATCHDAY_MULTIPLIER"]);

            string season = GetSeason(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
            var fixtures = GetFixtures(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
            var odds = GetOdds(apiUrl, leagueId, preferedBookmaker, xRapidApiKey, xRapidApiHost);
            var teams = GetTeams(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);

            MySqlCommand deleteOldHistoryOddsCommand = new MySqlCommand("DeleteOldHistoryOdds", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            deleteOldHistoryOddsCommand.ExecuteNonQuery();

            foreach (var odd in odds)
            {
                MySqlCommand updateHistoryOddsCommand = new MySqlCommand("UpdateHistoryOdds", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                updateHistoryOddsCommand.Parameters.Add(new MySqlParameter("IdMatchAPI", odd.Key));
                updateHistoryOddsCommand.Parameters.Add(new MySqlParameter("OddHome", odd.Value.OddHome));
                updateHistoryOddsCommand.Parameters.Add(new MySqlParameter("OddDraw", odd.Value.OddDraw));
                updateHistoryOddsCommand.Parameters.Add(new MySqlParameter("OddAway", odd.Value.OddAway));

                updateHistoryOddsCommand.ExecuteNonQuery();
            }

            foreach (var team in teams)
            {
                MySqlCommand updateTeamsCommand = new MySqlCommand();

                if (queriedTeams.Contains(team.TeamId))
                {
                    updateTeamsCommand = new MySqlCommand("UpdateTeam", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };
                }
                else
                {
                    updateTeamsCommand = new MySqlCommand("InsertTeam", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };
                }

                updateTeamsCommand.Parameters.Add(new MySqlParameter("TeamId", team.TeamId));
                updateTeamsCommand.Parameters.Add(new MySqlParameter("Name", team.Name));
                updateTeamsCommand.Parameters.Add(new MySqlParameter("LogoUri", team.LogoUri));

                updateTeamsCommand.ExecuteNonQuery();
            }

            foreach (var match in fixtures)
            {
                MySqlCommand command = new MySqlCommand();

                if (queriedFixtures.TryGetValue(match.FixtureId, out QueriedMatch.QueriedMatch queriedMatch))
                {
                    if (!queriedMatch.RequiresUpdate)
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

                    command = new MySqlCommand("UpdateMatch", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    _logger.LogInformation(
                        "[{0}]: Updating match data for FixtureId='{1}'...",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            match.FixtureId
                        });
                }
                else
                {
                    command = new MySqlCommand("InsertMatch", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    _logger.LogInformation(
                       "[{0}]: Inserting match data for FixtureId='{1}'...",
                       new object[]
                       {
                            DateTime.Now.ToString(),
                            match.FixtureId
                       });
                }

                double? oddHome = null;
                double? oddDraw = null;
                double? oddAway = null;
                if (odds.TryGetValue(match.FixtureId, out SimpleOdd.SimpleOdd odd))
                {
                    oddHome = odd.OddHome;
                    oddDraw = odd.OddDraw;
                    oddAway = odd.OddAway;
                }
                else
                {
                    if (queriedMatch != null && queriedMatch.SimpleOdd != null)
                    {
                        if (queriedMatch.SimpleOdd.OddHome != null)
                        {
                            oddHome = queriedMatch.SimpleOdd.OddHome;
                        }

                        if (queriedMatch.SimpleOdd.OddDraw != null)
                        {
                            oddDraw = queriedMatch.SimpleOdd.OddDraw;
                        }

                        if (queriedMatch.SimpleOdd.OddAway != null)
                        {
                            oddAway = queriedMatch.SimpleOdd.OddAway;
                        }
                    }
                }

                string result = null;
                if (match.StatusShort == "FT")
                {
                    result = ProcessResult(match.GoalsHomeTeam, match.GoalsAwayTeam);
                }

                command.Parameters.Add(new MySqlParameter("Matchday", match.Round.Split('-')[1][1..]));
                command.Parameters.Add(new MySqlParameter("Hometeam", match.HomeTeam.TeamName));
                command.Parameters.Add(new MySqlParameter("Hometeamgoals", match.GoalsHomeTeam));
                command.Parameters.Add(new MySqlParameter("Awayteam", match.AwayTeam.TeamName));
                command.Parameters.Add(new MySqlParameter("Awayteamgoals", match.GoalsAwayTeam));
                command.Parameters.Add(new MySqlParameter("Idawayteam", match.AwayTeam.TeamId));
                command.Parameters.Add(new MySqlParameter("Idhometeam", match.HomeTeam.TeamId));
                command.Parameters.Add(new MySqlParameter("Status", match.StatusShort));
                command.Parameters.Add(new MySqlParameter("Competitionyear", season));
                command.Parameters.Add(new MySqlParameter("UtcDate", match.EventDate.UtcDateTime));
                command.Parameters.Add(new MySqlParameter("IdmatchAPI", match.FixtureId));
                command.Parameters.Add(new MySqlParameter("Result1", result));
                command.Parameters.Add(new MySqlParameter("Oddshome", oddHome));
                command.Parameters.Add(new MySqlParameter("Oddsdraw", oddDraw));
                command.Parameters.Add(new MySqlParameter("Oddsaway", oddAway));

                command.ExecuteNonQuery();
            }

            foreach (var unprocessedBet in unprocessedBets)
            {
                if (queriedFixtures.TryGetValue(unprocessedBet, out QueriedMatch.QueriedMatch queriedMatch))
                {
                    if (queriedMatch.RequiresUpdate)
                    {
                        continue;
                    }

                    double? matchScore = GetMatchScore(
                        numberMatchdayMultiplier,
                        queriedMatch);

                    if (matchScore == null)
                    {
                        _logger.LogError(
                           "[{0}]: No valid odd for FixtureId='{1}'. Skipping bet update...",
                           new object[]
                           {
                                    DateTime.Now.ToString(),
                                    unprocessedBet
                           });
                    }
                    else
                    {
                        MySqlCommand updateUnprocessedBetsCommand = new MySqlCommand();

                        updateUnprocessedBetsCommand = new MySqlCommand("UpdateUnprocessedBets", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };

                        updateUnprocessedBetsCommand.Parameters.Add(new MySqlParameter("IdmatchAPI", unprocessedBet));
                        updateUnprocessedBetsCommand.Parameters.Add(new MySqlParameter("Result", queriedMatch.Result));
                        updateUnprocessedBetsCommand.Parameters.Add(new MySqlParameter("Score", matchScore));

                        updateUnprocessedBetsCommand.ExecuteNonQuery();
                    }
                }
            }

            MySqlCommand updateUserScoresCommand = new MySqlCommand();

            updateUserScoresCommand = new MySqlCommand("UpdateUserScores", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            updateUserScoresCommand.ExecuteNonQuery();

            _logger.LogInformation("[{0}]: Closing connection to MySQL...", DateTime.Now.ToString());
            connection.Close();
            _logger.LogInformation("[{0}]: Connection to MySQL successful closed!", DateTime.Now.ToString());

            return Task.CompletedTask;
        }

        private string GetRandomResult()
        {
            Random random = new Random();
            string chars = "HDA";
            return new string(Enumerable.Repeat(chars, 1)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private List<string> GetAvailableTeams(MySqlConnection connection)
        {
            MySqlCommand availableTeamsCommand = new MySqlCommand("GetAvailableTeams", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader availableTeamsReader = availableTeamsCommand.ExecuteReader();

            List<string> queriedTeams = new List<string>();
            while (availableTeamsReader.Read())
            {
                queriedTeams.Add(availableTeamsReader.GetString("TeamId"));
            }

            availableTeamsReader.Close();

            return queriedTeams;
        }

        private List<Teams.Team> GetTeams(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var teamsUrl = new RestClient(apiUrl + "/v2/teams/league/" + leagueId);
            var teamsRequest = new RestRequest(Method.GET);
            teamsRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            teamsRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse teamsResponse = teamsUrl.Execute(teamsRequest);

            var parsedTeams = JsonConvert.DeserializeObject<Teams.Teams>(teamsResponse.Content);

            return parsedTeams.Api.Teams;
        }

        private double? GetMatchScore(int numberMatchdayMultiplier, QueriedMatch.QueriedMatch queriedMatch)
        {
            double multiplier = 1;

            if (queriedMatch.Matchday >= numberMatchdayMultiplier)
            {
                multiplier = 2;
            }

            double? points;
            if (queriedMatch.Result == "H")
            {
                points = multiplier * queriedMatch.SimpleOdd.OddHome;
            }
            else if (queriedMatch.Result == "D")
            {
                points = multiplier * queriedMatch.SimpleOdd.OddDraw;
            }
            else if (queriedMatch.Result == "A")
            {
                points = multiplier * queriedMatch.SimpleOdd.OddAway;
            }
            else
            {
                return null;
            }

            if (points == null)
            {
                return null;
            }
            else
            {
                return Math.Round((double)points, 2);
            }
        }

        private List<string> GetUnprocessedBets(MySqlConnection connection)
        {
            MySqlCommand unprocessedBetsCommand = new MySqlCommand("GetUnprocessedBets", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader unprocessedBetsReader = unprocessedBetsCommand.ExecuteReader();

            List<string> unprocessedBets = new List<string>();
            while (unprocessedBetsReader.Read())
            {
                unprocessedBets.Add(unprocessedBetsReader["Idmatch"].ToString());
            }

            unprocessedBetsReader.Close();

            return unprocessedBets;
        }

        private static Dictionary<string, QueriedMatch.QueriedMatch> GetAvailableFixtures(MySqlConnection connection)
        {
            MySqlCommand availableFixturesCommand = new MySqlCommand("GetAvailableFixtures", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader availableFixturesReader = availableFixturesCommand.ExecuteReader();

            Dictionary<string, QueriedMatch.QueriedMatch> queriedFixtures = new Dictionary<string, QueriedMatch.QueriedMatch>();
            while (availableFixturesReader.Read())
            {
                queriedFixtures.Add(
                    availableFixturesReader["IdmatchAPI"].ToString(),
                    new QueriedMatch.QueriedMatch()
                    {
                        RequiresUpdate = availableFixturesReader.IsDBNull("Result1"),
                        Result = ProcessMySqlResult(availableFixturesReader, "Result1"),
                        Matchday = ProcessMySqlMatchday(availableFixturesReader, "Matchday"),
                        SimpleOdd = new SimpleOdd.SimpleOdd()
                        {
                            OddHome = ProcessMySqlOdd(availableFixturesReader, "Oddshome"),
                            OddDraw = ProcessMySqlOdd(availableFixturesReader, "Oddsdraw"),
                            OddAway = ProcessMySqlOdd(availableFixturesReader, "Oddsaway")
                        }
                    });
            }

            availableFixturesReader.Close();

            return queriedFixtures;
        }

        private static int? ProcessMySqlMatchday(MySqlDataReader availableFixturesReader, string column)
        {
            if (availableFixturesReader.IsDBNull(column))
            {
                return null;
            }
            else
            {
                return availableFixturesReader.GetInt32(column);
            }
        }

        private static string ProcessMySqlResult(MySqlDataReader availableFixturesReader, string column)
        {
            if (availableFixturesReader.IsDBNull(column))
            {
                return null;
            }
            else
            {
                return availableFixturesReader.GetString(column);
            }
        }

        private static double? ProcessMySqlOdd(MySqlDataReader odd, string column)
        {
            if (odd.IsDBNull(column))
            {
                return null;
            }
            else
            {
                return (double?)odd.GetDecimal(column);
            }
        }

        private static Dictionary<string, string> GetGlobalConstants(MySqlConnection connection)
        {
            MySqlCommand globalConstantsCommand = new MySqlCommand("GetGlobalConstants", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader globalConstantsReader = globalConstantsCommand.ExecuteReader();

            Dictionary<string, string> globalConstants = new Dictionary<string, string>();
            while (globalConstantsReader.Read())
            {
                globalConstants.Add(
                    globalConstantsReader["Constant"].ToString(),
                    globalConstantsReader["Value"].ToString());
            }

            globalConstantsReader.Close();

            return globalConstants;
        }

        private string GetSeason(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var leaguesUrl = new RestClient(apiUrl + "/v2/leagues/league/" + leagueId);
            var leaguesRequest = new RestRequest(Method.GET);
            leaguesRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            leaguesRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse leaguesResponse = leaguesUrl.Execute(leaguesRequest);

            var parsedLeagues = JsonConvert.DeserializeObject<Leagues.Leagues>(leaguesResponse.Content);

            return parsedLeagues.Api.Leagues.FirstOrDefault().Season;
        }

        private string ProcessResult(int? goalsHomeTeam, int? goalsAwayTeam)
        {
            if (goalsHomeTeam > goalsAwayTeam)
            {
                return "H";
            }
            else if (goalsHomeTeam == goalsAwayTeam)
            {
                return "D";
            }
            else if (goalsHomeTeam < goalsAwayTeam)
            {
                return "A";
            }

            return null;
        }

        private static Dictionary<string, SimpleOdd.SimpleOdd> GetOdds(string apiUrl, string leagueId, string bookmakerId, string xRapidApiKey, string xRapidApiHost)
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

            return odds
                .ToDictionary(
                    odd => odd.Fixture.FixtureId,
                    odd =>
                    {
                        Bookmaker bookmaker = odd.Bookmakers.FirstOrDefault(auxBookmaker => auxBookmaker.BookmakerId == bookmakerId);

                        if (bookmaker == null)
                        {
                            return null;
                        }

                        return new SimpleOdd.SimpleOdd(bookmaker);
                    })
                .Where(kvp => kvp.Value != null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
