using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bent.Bot.Apis.LastFm;
using Bent.Bot.Configuration;

namespace Bent.Bot.Module
{
    // TODO: break out classes

    [Export(typeof(IModule))]
    public class LastFm : IModule
    {
        #region Regular Expressions

        private static Regex musicRegex = new Regex(@"^\s*music\s+(.+?)\s*\.?\s*$", RegexOptions.IgnoreCase);
        private static Regex similarTrackRegex = new Regex(@"^\s*similar\s+to\s+""(.+)""\s+by\s+(.+)\s*$", RegexOptions.IgnoreCase);
        private static Regex similarArtistRegex = new Regex(@"^\s*similar\s+to\s+(.+)\s*$", RegexOptions.IgnoreCase);
        private static Regex discoveryChainArtistRegex = new Regex(@"^\s*discovery\s+(.+)\s*$", RegexOptions.IgnoreCase);
        private static Regex discoveryChainTrackRegex = new Regex(@"^\s*discovery\s+""(.+)""\s+by\s+(.+)\s*$", RegexOptions.IgnoreCase);
        private static Regex hypedTracksRegex = new Regex(@"^\s*hyped\s+tracks\s*$", RegexOptions.IgnoreCase);
        private static Regex topTracksRegex = new Regex(@"^\s*top\s+tracks\s*$", RegexOptions.IgnoreCase);
        private static Regex helpRegex = new Regex(@"^\s*help\s*$", RegexOptions.IgnoreCase);

        #endregion

        #region Fields

        private IBackend backend;
        private IConfiguration config;

        #endregion

        #region IModule Members

        public void OnStart(IConfiguration config, IBackend backend)
        {
            this.config = config;
            this.backend = backend;
        }

        public void OnMessage(IMessage message)
        {
            TestMusic(message);
        }

        #endregion

        #region Regex Tests

        private async void TestMusic(IMessage message)
        {
            try
            {
                if (message.IsRelevant && !message.IsHistorical)
                {
                    var musicMatch = musicRegex.Match(message.Body);
                    var musicBody = musicMatch.Groups[1].Value;
                    if (musicMatch.Success)
                    {
                        if (await TestSimilarTracks  (message, musicBody)) return;
                        if (await TestSimilarArtists (message, musicBody)) return;
                        if (await TestTrackDiscovery (message, musicBody)) return;
                        if (await TestArtistDiscovery(message, musicBody)) return;
                        if (await TestHypedTracks    (message, musicBody)) return;
                        if (await TestTopTracks      (message, musicBody)) return;
                        if (await TestHelp           (message, musicBody)) return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task<bool> TestSimilarTracks(IMessage message, string musicBody)
        {
            var similarTrackMatch = similarTrackRegex.Match(musicBody);
            if (similarTrackMatch.Success)
            {
                string track = similarTrackMatch.Groups[1].Value;
                string artist = similarTrackMatch.Groups[2].Value;
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetSimilarTracksAsync(artist, track);
                await backend.SendMessageAsync(message.ReplyTo, LastFmResponse.CreateSimilarTracksResponse(xml));
                return true;
            }
            return false;
        }

        private async Task<bool> TestSimilarArtists(IMessage message, string body)
        {
            var similarArtistMatch = similarArtistRegex.Match(body);
            if (similarArtistMatch.Success)
            {
                string artist = similarArtistMatch.Groups[1].Value;
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetSimilarArtistsAsync(artist);
                await backend.SendMessageAsync(message.ReplyTo, LastFmResponse.CreateSimilarArtistsResponse(xml));
                return true;
            }
            return false;
        }

        private async Task<bool> TestTrackDiscovery(IMessage message, string body)
        {
            var discoveryChainTrackMatch = discoveryChainTrackRegex.Match(body);
            if (discoveryChainTrackMatch.Success)
            {
                string track = discoveryChainTrackMatch.Groups[1].Value;
                string artist = discoveryChainTrackMatch.Groups[2].Value;
                await backend.SendMessageAsync(message.ReplyTo, "Looking for cool stuff. Please be patient.");
                List<Track> discovered = await DiscoveryChainTrackLoop(artist, track, 10);
                await backend.SendMessageAsync(message.ReplyTo, "Discovery chain:\r\n" + String.Join(" ->\r\n", discovered));
                return true;
            }
            return false;
        }

        private async Task<bool> TestArtistDiscovery(IMessage message, string body)
        {
            var discoveryChainArtistMatch = discoveryChainArtistRegex.Match(body);
            if (discoveryChainArtistMatch.Success)
            {
                string artist = discoveryChainArtistMatch.Groups[1].Value;
                await backend.SendMessageAsync(message.ReplyTo, "Looking for cool stuff. Please be patient.");
                List<Artist> discovered = await DiscoveryChainArtistLoop(artist, 10);
                await backend.SendMessageAsync(message.ReplyTo, "Discovery chain: " + String.Join(" -> ", discovered));
                return true;
            }
            return false;
        }

        private async Task<bool> TestHypedTracks(IMessage message, string body)
        {
            if (hypedTracksRegex.Match(body).Success)
            {
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetHypedTracksAsync();
                await backend.SendMessageAsync(message.ReplyTo, LastFmResponse.CreateHypedTracksResponse(xml));
                return true;
            }
            return false;
        }

        private async Task<bool> TestTopTracks(IMessage message, string body)
        {
            if (topTracksRegex.Match(body).Success)
            {
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetTopTracksAsync();
                await backend.SendMessageAsync(message.ReplyTo, LastFmResponse.CreateTopTracksResponse(xml));
                return true;
            }
            return false;
        }

        private async Task<bool> TestHelp(IMessage message, string body)
        {
            var helpMatch = helpRegex.Match(body);
            if (helpMatch.Success)
            {
                await backend.SendMessageAsync(message.ReplyTo, LastFmResponse.CreateHelpResponse(config.Name));
                return true;
            }
            return false;
        }

        #endregion

        #region Web Service Loops

        // TODO: prevent cycles
        private async Task<List<Track>> DiscoveryChainTrackLoop(string artist, string track, int iterations)
        {
            Debug.Assert(iterations <= 10);

            var discovered = new List<Track>();
            var originalTrackName = new Track(artist, track);
            for (int i = 0; i < iterations; i++)
            {
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetSimilarTracksAsync(originalTrackName.Artist, originalTrackName.TrackName);
                List<Track> similar = LastFmXmlParser.GetSimilarTracks(xml, out originalTrackName, true, 1);

                if (i == 0)
                {
                    discovered.Add(originalTrackName);
                }

                if (similar.Any())
                {
                    discovered.Add(similar.First());
                }
                else
                {
                    break;
                }
            }

            return discovered.ToList();
        }

        // TODO: prevent cycles
        private async Task<List<Artist>> DiscoveryChainArtistLoop(string artist, int iterations)
        {
            Debug.Assert(iterations <= 10);

            var discovered = new List<Artist>();

            string originalArtistName = artist;
            for (int i = 0; i < iterations; i++)
            {
                XDocument xml = await new LastFmClient(this.config[Common.Constants.ConfigKey.LastFmApiKey]).GetSimilarArtistsAsync(originalArtistName);
                List<Artist> similar = LastFmXmlParser.GetSimilarArtistNames(xml, out originalArtistName, true, 1);

                if (i == 0)
                {
                    discovered.Add(new Artist(originalArtistName));
                }

                if (similar.Any())
                {
                    discovered.Add(similar.First());
                    originalArtistName = similar.First().Name;
                }
                else
                {
                    break;
                }
            }

            return discovered;
        }

        #endregion

        #region Private Classes

        private class Artist
        {
            public string Name { get; private set; }

            public Artist(string name)
            {
                this.Name = name;
            }

            public override string ToString()
            {
                return this.Name;
            }
        }

        private class Track
        {
            public string Artist { get; private set; }
            public string TrackName { get; private set; }

            public Track(string artist, string trackName)
            {
                this.Artist = artist;
                this.TrackName = trackName;
            }

            public override string ToString()
            {
                return String.Format("\"{0}\" by {1}", TrackName, Artist);
            }
        }

        private static class LastFmXmlParser
        {
            public static List<Artist> GetSimilarArtistNames(XDocument xml, out string originalArtistName, bool isRandomized = true, int limit = 10)
            {
                Debug.Assert(limit > 0);

                originalArtistName = xml
                    .Descendants("similarartists").First()
                    .Attribute("artist").Value;
                
                var r = new Random();
                var names = new List<Artist>();
                foreach (var item in xml.Descendants("artist").OrderBy(x => isRandomized ? r.Next() : 0).Take(limit))
                {
                    names.Add(new Artist(item.Element("name").Value));
                }

                return names;
            }

            public static List<Track> GetSimilarTracks(XDocument xml, out Track originalTrackName, bool isRandomized = false, int limit = 25)
            {
                Debug.Assert(limit > 0);

                var similarTracksElement = xml.Descendants("similartracks").First();
                originalTrackName = new Track(
                    similarTracksElement.Attribute("artist").Value,
                    similarTracksElement.Attribute("track").Value
                );

                return GetTracks(xml, isRandomized, limit);
            }

            public static List<Track> GetTracks(XDocument xml, bool isRandomized = true, int limit = 10)
            {
                var r = new Random();
                var tracks = new List<Track>();
                foreach (var item in xml.Descendants("track").OrderBy(x => isRandomized ? r.Next() : 0).Take(limit))
                {
                    tracks.Add(new Track(
                        item.Element("artist").Element("name").Value,
                        item.Element("name").Value));
                }

                return tracks;
            }
        }

        private static class LastFmResponse
        {
            public static string CreateSimilarArtistsResponse(XDocument xml, bool isRandomized = true, int limit = 10)
            {
                string originalArtistName;
                List<Artist> similarArtists = LastFmXmlParser.GetSimilarArtistNames(xml, out originalArtistName, isRandomized, limit);

                var response = new StringBuilder();
                response
                    .Append("Similar artists to ")
                    .Append(originalArtistName)
                    .Append(": ")
                    .Append(String.Join(", ", similarArtists))
                    .Append(".");

                return response.ToString();
            }

            public static string CreateSimilarTracksResponse(XDocument xml, bool isRandomized = false, int limit = 25)
            {
                Track originalTrack;
                List<Track> similarTrackNames = LastFmXmlParser.GetSimilarTracks(xml, out originalTrack, isRandomized, limit);

                var response = new StringBuilder();
                response
                    .Append("Similar songs to ")
                    .Append(originalTrack)
                    .Append(":\r\n")
                    .Append(String.Join("\r\n", similarTrackNames));

                return response.ToString();
            }

            public static string CreateHypedTracksResponse(XDocument xml, bool isRandomized = false, int limit = 25)
            {
                List<Track> trackNames = LastFmXmlParser.GetTracks(xml, isRandomized, limit);

                var response = new StringBuilder();
                response
                    .Append("Hyped tracks:\r\n")
                    .Append(String.Join("\r\n", trackNames));

                return response.ToString();
            }

            public static string CreateTopTracksResponse(XDocument xml, bool isRandomized = false, int limit = 25)
            {
                List<Track> trackNames = LastFmXmlParser.GetTracks(xml, isRandomized, limit);

                var response = new StringBuilder();
                response
                    .Append("Top tracks:\r\n")
                    .Append(String.Join("\r\n", trackNames));

                return response.ToString();
            }

            public static string CreateHelpResponse(string botName)
            {
                var response = new StringBuilder();

                response.AppendLine();
                response.AppendLine(botName + " music help");
                response.AppendLine("    The help text you are currently viewing.");
                response.AppendLine();
                response.AppendLine(botName + " music similar to Rebecca Black");
                response.AppendLine("    Returns a randomized list of artists that are similar to Rebecca Black.");
                response.AppendLine();
                response.AppendLine(botName + " music similar to \"Whip My Hair\" by Willow Smith");
                response.AppendLine("    Returns a list of songs that are similar to \"Whip My Hair\" by Willow Smith, sorted by relevance.");
                response.AppendLine();
                response.AppendLine(botName + " music discovery Miley Cyrus");
                response.AppendLine("    Returns a discovery chain of artists, beginning with Miley Cyrus.");
                response.AppendLine();
                response.AppendLine(botName + " music discovery \"Ice Ice Baby\" by Vanilla Ice");
                response.AppendLine("    Returns a discovery chain of songs, beginning with \"Ice Ice Baby\" by Vanilla Ice.");
                response.AppendLine();
                response.AppendLine(botName + " music top tracks");
                response.AppendLine("    Returns the most popular songs on Last.fm.");
                response.AppendLine();
                response.AppendLine(botName + " music hyped tracks");
                response.AppendLine("    Returns the fastest rising songs on Last.fm.");
                response.AppendLine();
                response.AppendLine();
                response.AppendLine("More cool features coming soon!");

                return response.ToString();
            }
        }

        #endregion
    }
}
