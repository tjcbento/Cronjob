using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Odds;
using RestSharp;

namespace Cronjob
{
    public class Program
    {
        public static void Main()
        {
            Directory.CreateDirectory(@"/logs");

            StringBuilder logOutput = new StringBuilder();

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
                string apiUrl = globalConstants["API_URL"].TrimEnd('/');
                string preferedBookmaker = globalConstants["PREFERED_BOOKMAKER"];
                string xRapidApiKey = globalConstants["X_RAPID_API_KEY"];
                string xRapidApiHost = globalConstants["X_RAPID_API_HOST"];
                string roundsImport = globalConstants["ROUNDS_IMPORT"];

                logOutput.AppendLine(String.Format("[{0}]       [-] Getting current season from API", DateTime.Now.ToString()));
                string season = GetSeason(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting fixtures from API", DateTime.Now.ToString()));
                var fixtures = GetFixtures(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting odds from API", DateTime.Now.ToString()));
                var odds = GetOdds(apiUrl, leagueId, preferedBookmaker, xRapidApiKey, xRapidApiHost);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting teams from API", DateTime.Now.ToString()));
                var teams = GetTeams(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);
                logOutput.AppendLine(String.Format("[{0}]       [-] Getting standings from API", DateTime.Now.ToString()));
                var standings = GetStandings(apiUrl, leagueId, xRapidApiKey, xRapidApiHost);

                MySqlCommand truncateStandingsCommand = new MySqlCommand("TruncateStandings", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                logOutput.AppendLine(String.Format("[{0}]       [+] Truncating standings", DateTime.Now.ToString()));
                truncateStandingsCommand.ExecuteNonQuery();

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating standings", DateTime.Now.ToString()));
                foreach (var standing in standings)
                {
                    MySqlCommand updateStandingCommand = new MySqlCommand("UpdateStanding", connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                    updateStandingCommand.Parameters.Add(new MySqlParameter("TeamId", standing.TeamId));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Position", standing.Position));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Points", standing.Points));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Forme", ProcessForme(standing.Forme)));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("MatchesPlayed", standing.All.MatchesPlayed));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Win", standing.All.Win));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Draw", standing.All.Draw));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("Lose", standing.All.Lose));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("GoalsFor", standing.All.GoalsFor));
                    updateStandingCommand.Parameters.Add(new MySqlParameter("GoalsAgainst", standing.All.GoalsAgainst));

                    logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Adding standing for TeamId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            standing.TeamId
                        }));
                    updateStandingCommand.ExecuteNonQuery();
                }

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

                    if (queriedTeams.Contains(team.TeamId))
                    {
                        logOutput.AppendLine(String.Format(
                        "[{0}]       [+] Updating team for TeamId='{1}'",
                        new object[]
                        {
                            DateTime.Now.ToString(),
                            team.TeamId
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
                            team.TeamId
                        }));
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

                logOutput.AppendLine(String.Format("[{0}]       [-] Updating matches", DateTime.Now.ToString()));
                foreach (var match in fixtures)
                {
                    MySqlCommand command = new MySqlCommand();

                    if (!match.Round.Contains(roundsImport))
                    {
                        logOutput.AppendLine(String.Format(
                                "[{0}]       [-] Skipping match update for FixtureId='{1}' (Not interested in matches for round '" + match.Round + "')",
                                new object[]
                                {
                                DateTime.Now.ToString(),
                                match.FixtureId
                                }));

                        continue;
                    }

                    if (queriedFixtures.TryGetValue(match.FixtureId, out QueriedMatch.QueriedMatch queriedMatch))
                    {
                        if (!queriedMatch.RequiresUpdate)
                        {
                            logOutput.AppendLine(String.Format(
                                "[{0}]       [-] Skipping match update for FixtureId='{1}' (Already up-to-date)",
                                new object[]
                                {
                                DateTime.Now.ToString(),
                                match.FixtureId
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
                            match.FixtureId
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
                            match.FixtureId
                           }));
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

                    command.Parameters.Add(new MySqlParameter("LeagueID", leagueId));
                    command.Parameters.Add(new MySqlParameter("Matchday", match.Round.Split('-')[1][1..]));
                    command.Parameters.Add(new MySqlParameter("Multiplier", multiplier[Convert.ToInt32(match.Round.Split('-')[1][1..])]));
                    command.Parameters.Add(new MySqlParameter("Hometeam", match.HomeTeam.TeamName));
                    command.Parameters.Add(new MySqlParameter("Hometeamgoals", match.GoalsHomeTeam));
                    command.Parameters.Add(new MySqlParameter("Awayteam", match.AwayTeam.TeamName));
                    command.Parameters.Add(new MySqlParameter("Awayteamgoals", match.GoalsAwayTeam));
                    command.Parameters.Add(new MySqlParameter("Idawayteam", match.AwayTeam.TeamId));
                    command.Parameters.Add(new MySqlParameter("Idhometeam", match.HomeTeam.TeamId));
                    command.Parameters.Add(new MySqlParameter("Status", match.StatusShort));
                    command.Parameters.Add(new MySqlParameter("Competitionyear", season));
                    command.Parameters.Add(new MySqlParameter("UtcDate", match.EventDate.UtcDateTime.AddHours(1)));
                    command.Parameters.Add(new MySqlParameter("IdmatchAPI", match.FixtureId));
                    command.Parameters.Add(new MySqlParameter("Result1", result));
                    command.Parameters.Add(new MySqlParameter("Oddshome", oddHome));
                    command.Parameters.Add(new MySqlParameter("Oddsdraw", oddDraw));
                    command.Parameters.Add(new MySqlParameter("Oddsaway", oddAway));

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
                CheckForMissingOdds(logOutput, connection);

                if (fixtures.All(match => match.StatusShort == "FT") && !Convert.ToBoolean(globalConstants["EMAIL_LEAGUE_MANAGERS"]))
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

            ProcessLogs(logOutput.ToString());
        }

        private static string ProcessForme(string forme)
        {
            return new string(forme.Replace('W', 'C').Replace('D', 'E').Replace('L', 'D').Reverse().ToArray());
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

        private static List<Standings.Standing> GetStandings(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var leaguesUrl = new RestClient(apiUrl + "/v2/leagueTable/" + leagueId);
            var leaguesRequest = new RestRequest(Method.GET);
            leaguesRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            leaguesRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse standingsResponse = leaguesUrl.Execute(leaguesRequest);

            var parsedStandings = JsonConvert.DeserializeObject<Standings.Standings>(standingsResponse.Content);

            return parsedStandings.Api.Standings[0];
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

        private static void CheckForMissingOdds(StringBuilder logOutput, MySqlConnection connection)
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

        private static void ProcessLogs(string logOutput)
        {
            string logOutputPath = "/logs/log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";

            File.AppendAllText(logOutputPath, logOutput);

            if (logOutput.Contains("[!]"))
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

        private static List<Teams.Team> GetTeams(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var teamsUrl = new RestClient(apiUrl + "/v2/teams/league/" + leagueId);
            var teamsRequest = new RestRequest(Method.GET);
            teamsRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            teamsRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse teamsResponse = teamsUrl.Execute(teamsRequest);

            var parsedTeams = JsonConvert.DeserializeObject<Teams.Teams>(teamsResponse.Content);

            return parsedTeams.Api.Teams;
        }

        private static double? GetMatchScore(QueriedMatch.QueriedMatch queriedMatch)
        {
            double? points;
            if (queriedMatch.Result == "H")
            {
                points = queriedMatch.Multiplier * queriedMatch.SimpleOdd.OddHome;
            }
            else if (queriedMatch.Result == "D")
            {
                points = queriedMatch.Multiplier * queriedMatch.SimpleOdd.OddDraw;
            }
            else if (queriedMatch.Result == "A")
            {
                points = queriedMatch.Multiplier * queriedMatch.SimpleOdd.OddAway;
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
                return Math.Round((double)points, 2, MidpointRounding.AwayFromZero);
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

        private static string GetSeason(string apiUrl, string leagueId, string xRapidApiKey, string xRapidApiHost)
        {
            var leaguesUrl = new RestClient(apiUrl + "/v2/leagues/league/" + leagueId);
            var leaguesRequest = new RestRequest(Method.GET);
            leaguesRequest.AddHeader("X-RAPIDAPI-KEY", xRapidApiKey);
            leaguesRequest.AddHeader("X-RAPIDAPI-HOST", xRapidApiHost);
            IRestResponse leaguesResponse = leaguesUrl.Execute(leaguesRequest);

            var parsedLeagues = JsonConvert.DeserializeObject<Leagues.Leagues>(leaguesResponse.Content);

            return parsedLeagues.Api.Leagues.FirstOrDefault().Season;
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