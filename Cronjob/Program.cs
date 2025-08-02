using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using Fixtures;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Odds;
using RestSharp;

namespace Cronjob
{
    public class Program
    {
        public static string leaguesResponseAttachment = "";
        public static string fixturesResponseAttachment = "";
        public static string oddsResponseAttachment = "";
        public static string teamsResponseAttachment = "";
        public static string standingsResponseAttachment = "";
        public static StringBuilder logOutput = new StringBuilder();

        public static void Main()
        {
            Directory.CreateDirectory(@"/logs");

            try
            {
                logOutput.AppendLine(String.Format("[{0}]       [-] Update script starting", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Retrieving necessary environment variables", DateTime.Now.ToString()));
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

                logOutput.AppendLine(String.Format("[{0}]       [-] Opening connection to MySQL", DateTime.Now.ToString()));
                MySqlConnection connection = new MySqlConnection(connectionString);
                connection.Open();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successfully opened", DateTime.Now.ToString()));

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting global constants", DateTime.Now.ToString()));
                Dictionary<string, string> globalConstants = GetGlobalConstants(connection);

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting available teams", DateTime.Now.ToString()));
                List<string> queriedTeams = GetAvailableTeams(connection);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting available fixtures", DateTime.Now.ToString()));
                Dictionary<string, QueriedMatch.QueriedMatch> queriedFixtures = GetAvailableFixtures(connection);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting unprocessed bets", DateTime.Now.ToString()));
                List<string> unprocessedBets = GetUnprocessedBets(connection);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting multipliers", DateTime.Now.ToString()));
                Dictionary<int, double?> multiplier = GetMultipliers(connection);

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting global constants", DateTime.Now.ToString()));
                string leagueId = globalConstants["LEAGUE_ID"];
                string apiSportsKey = globalConstants["X-APISPORTS-KEY"];
                string roundsImport = globalConstants["ROUNDS_IMPORT"];

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting fixtures from API", DateTime.Now.ToString()));
                var fixtures = GetFixtures(apiSportsKey);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting odds from API", DateTime.Now.ToString()));
                var odds = GetOdds(apiSportsKey);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting teams from API", DateTime.Now.ToString()));
                var teams = GetTeams(apiSportsKey);

                MySqlCommand truncateStandingsCommand = new MySqlCommand("TruncateStandings", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                logOutput.AppendLine(String.Format("[{0}]       [+] Truncating standings", DateTime.Now.ToString()));
                truncateStandingsCommand.ExecuteNonQuery();

                MySqlCommand deleteOldHistoryOddsCommand = new MySqlCommand("DeleteOldHistoryOdds", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                logOutput.AppendLine(String.Format("[{0}]       [+] Deleting old history odds", DateTime.Now.ToString()));
                deleteOldHistoryOddsCommand.ExecuteNonQuery();

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating history odds", DateTime.Now.ToString()));
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

                    logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Adding history odds for FixtureId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            odd.Key
                        }));
                    updateHistoryOddsCommand.ExecuteNonQuery();
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating teams", DateTime.Now.ToString()));
                foreach (var team in teams)
                {
                    MySqlCommand updateTeamsCommand = new MySqlCommand();

                    if (queriedTeams.Contains(team.Id))
                    {
                        logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Updating team for TeamId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            team.Id
                        }));
                        updateTeamsCommand = new MySqlCommand("UpdateTeam", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };
                    }
                    else
                    {
                        logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Inserting team with TeamId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            team.Id
                        }));
                        updateTeamsCommand = new MySqlCommand("InsertTeam", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };
                    }

                    updateTeamsCommand.Parameters.Add(new MySqlParameter("TeamId", team.Id));
                    updateTeamsCommand.Parameters.Add(new MySqlParameter("Name", team.Name));
                    updateTeamsCommand.Parameters.Add(new MySqlParameter("LogoUri", team.Logo));

                    updateTeamsCommand.ExecuteNonQuery();
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating matches", DateTime.Now.ToString()));
                foreach (var match in fixtures)
                {
                    MySqlCommand command = new MySqlCommand();

                    if (!match.League.Round.Contains(roundsImport))
                    {
                        logOutput.AppendLine(String.Format(
                                "[{0}]       [-] Skipping match update for FixtureId='{1}' (Not interested in matches for round '" + match.League.Round + "')",
                                new object[]
                                {
                                DateTime.Now.ToString(),
                                match.FixtureInfo.FixtureId
                                }));

                        continue;
                    }

                    if (queriedFixtures.TryGetValue(match.FixtureInfo.FixtureId, out QueriedMatch.QueriedMatch queriedMatch))
                    {
                        if (!queriedMatch.RequiresUpdate)
                        {
                            logOutput.AppendLine(String.Format(
                                "[{0}]       [-] Skipping match update for FixtureId='{1}' (Already up-to-date)",
                                new object[]
                                {
                                DateTime.Now.ToString(),
                                match.FixtureInfo.FixtureId
                                }));

                            continue;
                        }

                        command = new MySqlCommand("UpdateMatch", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };

                        logOutput.AppendLine(String.Format(
                            "[{0}]       [+] Updating match data for FixtureId='{1}'",
                            new object[]
                            {
                            DateTime.Now.ToString(),
                            match.FixtureInfo.FixtureId
                            }));
                    }
                    else
                    {
                        command = new MySqlCommand("InsertMatch", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };

                        logOutput.AppendLine(String.Format(
                           "[{0}]       [+] Inserting match with FixtureId='{1}'",
                           new object[]
                           {
                            DateTime.Now.ToString(),
                            match.FixtureInfo.FixtureId
                           }));
                    }

                    double? oddHome = null;
                    double? oddDraw = null;
                    double? oddAway = null;
                    if (odds.TryGetValue(match.FixtureInfo.FixtureId, out SimpleOdd.SimpleOdd odd))
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
                    string videoCode = null;
                    if (match.FixtureInfo.Status.Short == "FT")
                    {
                        result = ProcessResult(match.Goals.Home, match.Goals.Away);
                    }

                    command.Parameters.Add(new MySqlParameter("LeagueID", leagueId));
                    command.Parameters.Add(new MySqlParameter("Matchday", match.League.Round.Split('-')[1][1..]));
                    command.Parameters.Add(new MySqlParameter("Multiplier", multiplier[Convert.ToInt32(match.League.Round.Split('-')[1][1..])]));
                    command.Parameters.Add(new MySqlParameter("Hometeam", match.Teams.HomeTeam.Name));
                    command.Parameters.Add(new MySqlParameter("Hometeamgoals", match.Goals.Home));
                    command.Parameters.Add(new MySqlParameter("Awayteam", match.Teams.AwayTeam.Name));
                    command.Parameters.Add(new MySqlParameter("Awayteamgoals", match.Goals.Away));
                    command.Parameters.Add(new MySqlParameter("Idawayteam", match.Teams.AwayTeam.Id));
                    command.Parameters.Add(new MySqlParameter("Idhometeam", match.Teams.HomeTeam.Id));
                    command.Parameters.Add(new MySqlParameter("Status", match.FixtureInfo.Status.Short));
                    command.Parameters.Add(new MySqlParameter("Competitionyear", "2025"));
                    command.Parameters.Add(new MySqlParameter("UtcDate", match.FixtureInfo.EventDate.AddHours(DateTimeOffset.Now.Offset.TotalHours)));
                    command.Parameters.Add(new MySqlParameter("IdmatchAPI", match.FixtureInfo.FixtureId));
                    command.Parameters.Add(new MySqlParameter("Result1", result));
                    command.Parameters.Add(new MySqlParameter("Oddshome", oddHome));
                    command.Parameters.Add(new MySqlParameter("Oddsdraw", oddDraw));
                    command.Parameters.Add(new MySqlParameter("Oddsaway", oddAway));
                    command.Parameters.Add(new MySqlParameter("Video", ""));

                    command.ExecuteNonQuery();
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating available fixtures", DateTime.Now.ToString()));
                queriedFixtures = GetAvailableFixtures(connection);

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating bets", DateTime.Now.ToString()));
                foreach (var unprocessedBet in unprocessedBets)
                {
                    if (queriedFixtures.TryGetValue(unprocessedBet, out QueriedMatch.QueriedMatch queriedMatch))
                    {
                        if (queriedMatch.RequiresUpdate)
                        {
                            logOutput.AppendLine(String.Format(
                                "[{0}]       [-] Skipping update for bets with FixtureId='{1}'",
                                new object[]
                                {
                                    DateTime.Now.ToString(),
                                    unprocessedBet
                                }));
                            continue;
                        }

                        double? matchScore = GetMatchScore(queriedMatch);

                        if (matchScore == null)
                        {
                            logOutput.AppendLine(String.Format(
                               "[{0}]       [!] No valid odds for bets with FixtureId='{1}'",
                               new object[]
                               {
                                DateTime.Now.ToString(),
                                unprocessedBet
                               }));
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

                            logOutput.AppendLine(String.Format(
                                "[{0}]       [+] Updating bets with FixtureId='{1}'",
                                new object[]
                                {
                                    DateTime.Now.ToString(),
                                    unprocessedBet
                                }));
                            updateUnprocessedBetsCommand.ExecuteNonQuery();
                        }
                    }
                }

                logOutput.AppendLine(String.Format("[{0}]       [+] Updating cumulative scores", DateTime.Now.ToString()));
                UpdateCumulativeScores(connection, leagueId);

                logOutput.AppendLine(String.Format("[{0}]       [-] Checking for missing odds", DateTime.Now.ToString()));
                CheckForMissingOdds(connection);

                if (fixtures.All(match => match.FixtureInfo.Status.Short == "FT") && !Convert.ToBoolean(globalConstants["EMAIL_LEAGUE_MANAGERS"]))
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] Sending e-mails to league managers", DateTime.Now.ToString()));

                    Dictionary<string, string> leagues = GetLeagues(connection);

                    string emailHeader = File.ReadAllText("/app/EmailHeader");
                    string emailTable = File.ReadAllText("/app/EmailTable");
                    string emailFooter = File.ReadAllText("/app/EmailFooter");

                    logOutput.AppendLine(String.Format("[{0}]       [-] Establishing SMTP session", DateTime.Now.ToString()));
                    var fromAddress = new MailAddress(Environment.GetEnvironmentVariable("FROM_EMAIL"), Environment.GetEnvironmentVariable("FROM_NAME"));
                    string emailPassword = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

                    var smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        Port = 587,
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false,
                        Credentials = new NetworkCredential(fromAddress.Address, emailPassword)
                    };
                    logOutput.AppendLine(String.Format("[{0}]       [-] SMTP session established successfully", DateTime.Now.ToString()));

                    foreach (var league in leagues)
                    {
                        MySqlCommand getFinalScoresLeagueCommand = new MySqlCommand("GetFinalScores", connection)
                        {
                            CommandType = CommandType.StoredProcedure
                        };

                        getFinalScoresLeagueCommand.Parameters.Add(new MySqlParameter("LeagueId", leagueId));
                        getFinalScoresLeagueCommand.Parameters.Add(new MySqlParameter("LeagueName", league.Key));

                        MySqlDataReader getFinalScoresLeagueReader = getFinalScoresLeagueCommand.ExecuteReader();

                        string body = GetBody(emailHeader, emailTable, emailFooter, league.Key, getFinalScoresLeagueReader);
                        getFinalScoresLeagueReader.Close();

                        using var message = new MailMessage(fromAddress, new MailAddress(league.Value))
                        {
                            Subject = "Resultado final liga " + league.Key,
                            Body = body,
                            IsBodyHtml = true
                        };

                        logOutput.AppendLine(String.Format("[{0}]       [+] Sending e-mail to the manager of " + league.Key + " league", DateTime.Now.ToString()));
                        smtp.Send(message);
                    }

                    logOutput.AppendLine(String.Format("[{0}]       [+] Disabling e-mails to league managers", DateTime.Now.ToString()));
                    DisableEmailToLeagueManagers(connection);
                }
                else
                {
                    logOutput.AppendLine(String.Format("[{0}]       [-] No need to send e-mail to league managers", DateTime.Now.ToString()));
                }

                logOutput.AppendLine(String.Format("[{0}]       [-] Closing connection to MySQL", DateTime.Now.ToString()));
                connection.Close();
                logOutput.AppendLine(String.Format("[{0}]       [-] Connection to MySQL successful closed", DateTime.Now.ToString()));
                logOutput.AppendLine(String.Format("[{0}]       [-] Job finished", DateTime.Now.ToString()));
            }
            catch (Exception ex)
            {
                logOutput.AppendLine(String.Format("[{0}]       [!] Job failed with exception:", DateTime.Now.ToString()));
                logOutput.AppendLine(ex.ToString());
            }

            ProcessLogs();
        }

        private static string FullSeason(string season)
        {
            var shortSeason = Convert.ToInt32(season[2..]);
            return String.Join("/", shortSeason, shortSeason + 1);
        }

        

        private static string ProcessForme(string forme)
        {
            if (String.IsNullOrEmpty(forme))
            {
                return "-";
            }

            return new string(forme.Replace('W', 'V').Replace('D', 'E').Replace('L', 'D').Reverse().ToArray());
        }

        private static string GetBody(string emailHeader, string emailTable, string emailFooter, string league, MySqlDataReader places)
        {
            StringBuilder body = new StringBuilder();

            body.Append(emailHeader);
            body.Append(league);
            body.Append(emailTable);

            int position = 1;
            while (places.Read())
            {
                body.Append("<tr style=\"box-sizing: border-box; page-break-inside: avoid;\">");
                body.Append("<td>&nbsp;" + position + " </td>");
                body.Append("<td>&nbsp;" + places.GetString("Name") + " </td>");
                body.Append("<td>&nbsp;" + places.GetString("Score") + " </td>");
                body.Append("<td>&nbsp;" + places.GetString("CorrectPredictions") + " </td>");
                body.Append("</tr>");

                position++;
            }

            body.Append(emailFooter);

            return body.ToString();
        }

        private static void DisableEmailToLeagueManagers(MySqlConnection connection)
        {
            MySqlCommand disableEmailToLeagueManagersCommand = new MySqlCommand("DisableEmailToLeagueManagers", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            disableEmailToLeagueManagersCommand.ExecuteNonQuery();
        }

        private static Dictionary<string, string> GetLeagues(MySqlConnection connection)
        {
            MySqlCommand leaguesCommand = new MySqlCommand("GetLeagues", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader leaguesReader = leaguesCommand.ExecuteReader();

            Dictionary<string, string> leagues = new Dictionary<string, string>();
            while (leaguesReader.Read())
            {
                leagues.Add(Convert.ToString(leaguesReader["LeagueName"]), Convert.ToString(leaguesReader["ManagerEmail"]));
            }

            leaguesReader.Close();

            return leagues;
        }

        private static List<Standings.Standing> GetStandings(string apiUrl, string leagueId, string apiSportsKey)
        {
            var leaguesUrl = new RestClient(apiUrl + "/leagueTable/" + leagueId);
            var leaguesRequest = new RestRequest();
            leaguesRequest.AddHeader("X-APISPORTS-KEY", apiSportsKey);
            var standingsResponse = leaguesUrl.Execute(leaguesRequest);
            standingsResponseAttachment = standingsResponse.Content;

            var parsedStandings = JsonConvert.DeserializeObject<Standings.Standings>(standingsResponse.Content);

            if (parsedStandings.Api.Standings.Any())
            {
                return parsedStandings.Api.Standings[0];
            }

            return new List<Standings.Standing>();
        }

        private static void UpdateCumulativeScores(MySqlConnection connection, string leagueId)
        {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
            MySqlCommand deleteCumulativeScoresByLeagueIDCommand = new MySqlCommand();
#pragma warning restore IDE0059 // Unnecessary assignment of a value

            deleteCumulativeScoresByLeagueIDCommand = new MySqlCommand("DeleteCumulativeScoresByLeagueID", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            deleteCumulativeScoresByLeagueIDCommand.Parameters.Add(new MySqlParameter("LeagueID", leagueId));

            deleteCumulativeScoresByLeagueIDCommand.ExecuteNonQuery();

            MySqlCommand getUsersPerLeagueIdCommand = new MySqlCommand("GetUsersPerLeagueID", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            getUsersPerLeagueIdCommand.Parameters.Add(new MySqlParameter("LeagueID", leagueId));

            MySqlDataReader getUsersPerLeagueIdReader = getUsersPerLeagueIdCommand.ExecuteReader();

            List<string> users = new List<string>();
            while (getUsersPerLeagueIdReader.Read())
            {
                users.Add(Convert.ToString(getUsersPerLeagueIdReader["Username"]));
            }

            getUsersPerLeagueIdReader.Close();

            foreach (var user in users)
            {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
                MySqlCommand updateCumulativeScoresCommand = new MySqlCommand();
#pragma warning restore IDE0059 // Unnecessary assignment of a value

                updateCumulativeScoresCommand = new MySqlCommand("UpdateCumulativeScores", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                updateCumulativeScoresCommand.Parameters.Add(new MySqlParameter("Username", user));
                updateCumulativeScoresCommand.Parameters.Add(new MySqlParameter("LeagueID", leagueId));

                updateCumulativeScoresCommand.ExecuteNonQuery();
            }
        }

        private static Dictionary<int, double?> GetMultipliers(MySqlConnection connection)
        {
            MySqlCommand multipliersCommand = new MySqlCommand("GetMultipliers", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader multipliersReader = multipliersCommand.ExecuteReader();

            Dictionary<int, double?> multipliers = new Dictionary<int, double?>();
            while (multipliersReader.Read())
            {
                multipliers.Add(Convert.ToInt32(multipliersReader["Matchday"]), Convert.ToDouble(multipliersReader["Multiplier"]));
            }

            multipliersReader.Close();

            return multipliers;
        }

        private static void CheckForMissingOdds(MySqlConnection connection)
        {
            MySqlCommand missingOddsForCurrentMatchesCommand = new MySqlCommand("MissingOddsForCurrentMatches ", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader missingOddsForCurrentMatchesReader = missingOddsForCurrentMatchesCommand.ExecuteReader();

            while (missingOddsForCurrentMatchesReader.Read())
            {
                logOutput.AppendLine(String.Format(
                    "[{0}]       [!] Missing odds for FixtureId='{1}'",
                    new object[]
                    {
                        DateTime.Now.ToString(),
                        missingOddsForCurrentMatchesReader.GetString("IdmatchAPI")
                    }));
            }

            missingOddsForCurrentMatchesReader.Close();
        }

        private static void ProcessLogs()
        {
            string logOutputPath = "/logs/log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
            string leaguesResponsePath = "/logs/leagues_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string fixturesResponsePath = "/logs/fixtures_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string oddsResponsePath = "/logs/odds_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string teamsResponsePath = "/logs/teams_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string standingsResponsePath = "/logs/standings_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            string logOutputString = logOutput.ToString();

            File.AppendAllText(logOutputPath, logOutputString);
            File.AppendAllText(leaguesResponsePath, leaguesResponseAttachment);
            File.AppendAllText(fixturesResponsePath, fixturesResponseAttachment);
            File.AppendAllText(oddsResponsePath, oddsResponseAttachment);
            File.AppendAllText(teamsResponsePath, teamsResponseAttachment);
            File.AppendAllText(standingsResponsePath, standingsResponseAttachment);

            if (logOutputString.Contains("[!]"))
            {
                var fromAddress = Environment.GetEnvironmentVariable("FROM_EMAIL");
                string operatorEmails = Environment.GetEnvironmentVariable("OPERATOR_EMAILS");
                string emailPassword = Environment.GetEnvironmentVariable("EMAIL_PASSWORD");

                var smtp = new SmtpClient
                {
                    Host = "smtp.gmail.com",
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(fromAddress, emailPassword)
                };

                using var message = new MailMessage(fromAddress, operatorEmails)
                {
                    Subject = "Errors in job!",
                    Body = "Hi operators,<br><br>An error has occured in today's scheduled job!<br>Log file has been attached to this message.",
                    IsBodyHtml = true,
                };

                message.Attachments.Add(new Attachment(logOutputPath));

                if (!String.IsNullOrEmpty(leaguesResponseAttachment))
                {
                    message.Attachments.Add(new Attachment(leaguesResponsePath));
                }
                if (!String.IsNullOrEmpty(fixturesResponseAttachment))
                {
                    message.Attachments.Add(new Attachment(fixturesResponsePath));
                }
                if (!String.IsNullOrEmpty(oddsResponseAttachment))
                {
                    message.Attachments.Add(new Attachment(oddsResponsePath));
                }
                if (!String.IsNullOrEmpty(teamsResponseAttachment))
                {
                    message.Attachments.Add(new Attachment(teamsResponsePath));
                }
                if (!String.IsNullOrEmpty(standingsResponseAttachment))
                {
                    message.Attachments.Add(new Attachment(standingsResponsePath));
                }

                smtp.Send(message);
            }

            string[] files = Directory.GetFiles("/logs");

            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);
                if (fileInfo.LastAccessTime < DateTime.Now.AddMonths(-1))
                {
                    fileInfo.Delete();
                }
            }
        }

        private static List<string> GetAvailableTeams(MySqlConnection connection)
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

        private static List<Teams.Team> GetTeams(string apiSportsKey)
        {
            var teamsUrl = new RestClient("https://v3.football.api-sports.io/teams?league=94&season=2025");
            var teamsRequest = new RestRequest();
            teamsRequest.AddHeader("X-APISPORTS-KEY", apiSportsKey);
            var teamsResponse = teamsUrl.Execute(teamsRequest);
            teamsResponseAttachment = teamsResponse.Content;

var parsedTeams = JsonConvert.DeserializeObject<Teams.TeamsApiResponse>(teamsResponse.Content);
return parsedTeams.Response.Select(r => r.Team).ToList();
        }

        private static double? GetMatchScore(QueriedMatch.QueriedMatch queriedMatch)
        {
            decimal? points;
            if (queriedMatch.Result == "H")
            {
                points = (decimal)queriedMatch.Multiplier * (decimal)queriedMatch.SimpleOdd.OddHome;
            }
            else if (queriedMatch.Result == "D")
            {
                points = (decimal)queriedMatch.Multiplier * (decimal)queriedMatch.SimpleOdd.OddDraw;
            }
            else if (queriedMatch.Result == "A")
            {
                points = (decimal)queriedMatch.Multiplier * (decimal)queriedMatch.SimpleOdd.OddAway;
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
                return Math.Round((double)points, 2, MidpointRounding.AwayFromZero); ;
            }
        }

        private static List<string> GetUnprocessedBets(MySqlConnection connection)
        {
            MySqlCommand unprocessedBetsCommand = new MySqlCommand("GetUnprocessedBets", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            MySqlDataReader unprocessedBetsReader = unprocessedBetsCommand.ExecuteReader();

            List<string> unprocessedBets = new List<string>();
            while (unprocessedBetsReader.Read())
            {
                unprocessedBets.Add(unprocessedBetsReader["IdmatchAPI"].ToString());
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
                        Multiplier = ProcessMySqlMultiplier(availableFixturesReader, "Multiplier"),
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

        private static double? ProcessMySqlMultiplier(MySqlDataReader availableFixturesReader, string column)
        {
            if (availableFixturesReader.IsDBNull(column))
            {
                return null;
            }
            else
            {
                return (double?)availableFixturesReader.GetDecimal(column);
            }
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

        private static double? ProcessMySqlOdd(MySqlDataReader availableFixturesReader, string column)
        {
            if (availableFixturesReader.IsDBNull(column))
            {
                return null;
            }
            else
            {
                return (double?)availableFixturesReader.GetDecimal(column);
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

        private static string ProcessResult(int? goalsHomeTeam, int? goalsAwayTeam)
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

        private static Dictionary<string, SimpleOdd.SimpleOdd> GetOdds(string apiSportsKey)
        {
            StringBuilder fullResponse = new StringBuilder();
            int page = 1;
            var oddsUrl = new RestClient("https://v3.football.api-sports.io/odds?league=94&season=2025&bookmaker=8&bet=1");
            var oddsRequest = new RestRequest();
            oddsRequest.AddHeader("X-APISPORTS-KEY", apiSportsKey);

            List<Odd> odds = new List<Odd>();
            string oddsResponseAttachment;


while (true)
{
    oddsRequest.AddOrUpdateParameter("page", page.ToString());
    var oddsResponse = oddsUrl.Execute(oddsRequest);
    fullResponse.AppendLine(oddsResponse.Content);

    var parsedOdds = JsonConvert.DeserializeObject<Odds.OddsApiResponse>(oddsResponse.Content);
    odds.AddRange(parsedOdds.Odds);

    if (parsedOdds.Paging.Current == parsedOdds.Paging.Total)
    {
        break;
    }

    page++;
}

            oddsResponseAttachment = fullResponse.ToString();

            return odds.ToDictionary(
    odd => odd.Fixture.Id.ToString(),
    odd =>
    {
        var values = odd.Bookmakers[0].Bets[0].Values;

        return new SimpleOdd.SimpleOdd
        {
            OddHome = double.Parse(values.First(v => v.Value == "Home").Odd),
            OddDraw = double.Parse(values.First(v => v.Value == "Draw").Odd),
            OddAway = double.Parse(values.First(v => v.Value == "Away").Odd)
        };
    });

        }

        private static List<Fixtures.Fixture> GetFixtures(string apiSportsKey)
        {
            var fixturesUrl = new RestClient("https://v3.football.api-sports.io/fixtures?league=94&season=2025");
            var fixturesRequest = new RestRequest();
            fixturesRequest.AddHeader("X-APISPORTS-KEY", apiSportsKey);
            var fixturesResponse = fixturesUrl.Execute(fixturesRequest);
            fixturesResponseAttachment = fixturesResponse.Content;

            var parsedResponse = JsonConvert.DeserializeObject<Fixtures.FixturesApiResponse>(fixturesResponse.Content);
            return parsedResponse.Response;
        }
    }
}