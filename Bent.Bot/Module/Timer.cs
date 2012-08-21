using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bent.Bot.Configuration;

namespace Bent.Bot.Module
{
    [Export(typeof(IModule))]
    public class Timer : IModule
    {
        // TODO: Allow user to specify exact time

        private static Regex timerRegex = new Regex(@"^\s*timer\s+(.+)\s*$", RegexOptions.IgnoreCase);
        private static Regex fromNowRegex = new Regex(@"^\s*(\-?[0-9\.]+)\s(ticks?|(swatch )?\.?beats?|minutes?|seconds?|hours?)\sfrom\snow\s+say\s+(.+)\s*$", RegexOptions.IgnoreCase);

        private IBackend backend;

        public void OnStart(IConfiguration config, IBackend backend)
        {
            this.backend = backend;
        }

        public void OnMessage(IMessage message)
        {
            TestTimer(message);
        }

        private void TestTimer(IMessage message)
        {
            if (message.IsRelevant && !message.IsHistorical)
            {
                var match = timerRegex.Match(message.Body);
                var timerBody = match.Groups[1].Value;
                if (match.Success)
                {
                    double milliseconds; string text;
                    if (TestTimeFromNow(timerBody, out milliseconds, out text))
                    {
                        if (!ValidateTime(message.ReplyTo, milliseconds)) return;
                        CreateTimer(message.ReplyTo, milliseconds, text);
                        this.backend.SendMessageAsync(message.ReplyTo, "Sure!");
                    }
                }
            }
        }

        private System.Timers.Timer CreateTimer(IAddress replyTo, double milliseconds, string text)
        {
            var t = new System.Timers.Timer();
            t.Interval = milliseconds;
            t.Elapsed += GetElapsedEvent(t, replyTo, text);
            t.Enabled = true;
            return t;
        }

        private bool ValidateTime(IAddress replyTo, double milliseconds)
        {
            if (milliseconds < 0)
            {
                this.backend.SendMessageAsync(replyTo, "I can't travel back in time, silly!");
                return false;
            }
            if (milliseconds == 0)
            {
                this.backend.SendMessageAsync(replyTo, "Sorry, something weird occurred. I couldn't set up that timer for you.");
                return false;
            }
            else if (milliseconds > 24 * 60 * 60 * 1000)
            {
                this.backend.SendMessageAsync(replyTo, "Let's keep the timer to within one day for now.");
                return false;
            }
            return true;
        }

        private bool TestTimeFromNow(string timerBody, out double milliseconds, out string text)
        {
            milliseconds = 0; text = "";
            
            var match = fromNowRegex.Match(timerBody);
            if (match.Success)
            {
                double num = 0;
                double.TryParse(match.Groups[1].Value, out num);
                
                text = match.Groups[4].Value;

                var units = match.Groups[2].Value;
                if (units.StartsWith("second"))
                {
                    milliseconds = num * 1000;
                }
                else if (units.StartsWith("minute"))
                {
                    milliseconds = num * 60 * 1000;
                }
                else if (units.StartsWith("hour"))
                {
                    milliseconds = num * 60 * 60 * 1000;
                }
                else if (units.StartsWith("tick"))
                {
                    milliseconds = num / TimeSpan.TicksPerMillisecond;
                }
                else if (units.Contains("beat"))
                {
                    milliseconds = num * 86.4 * 1000;
                }

                return true;
            }
            return false;
        }

        private System.Timers.ElapsedEventHandler GetElapsedEvent(System.Timers.Timer t,  IAddress replyTo, string text)
        {
            return (o, e) =>
            {
                t.Enabled = false;
                t.Dispose();

                this.backend.SendMessageAsync(replyTo, text);
            };
        }
    }
}
