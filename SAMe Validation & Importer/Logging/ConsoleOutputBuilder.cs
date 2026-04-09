using SAMe_VI.Object;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace SAMe_VI.Logging
{
    public class ConsoleOutputBuilder(TextWriter originalOut) : TextWriter
    {
        private readonly TextWriter _originalOut = originalOut;
        private readonly StringBuilder _notificationBuffer = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine()
        {
            _originalOut.WriteLine();
            _notificationBuffer.AppendLine();
        }
        public override void WriteLine(char value)
        {
            _originalOut.WriteLine(value);
            _notificationBuffer.AppendLine(value.ToString());
        }
        public override void WriteLine(string? value)
        {
            _originalOut.WriteLine(value);
            _notificationBuffer.AppendLine(value);
        }
        public override void WriteLine(object? value)
        {
            _originalOut.WriteLine(value);
            _notificationBuffer.AppendLine(value?.ToString() ?? string.Empty);
        }
        public string GetCapturedText() => _notificationBuffer.ToString();

        public async Task<bool?> PostRunReportAsync()
        {
            bool emailSent = false, fileWritten = false;
            try
            {
                KeyValuePair<string, string>? emailCredentials = Configuration.EmailCredentials;
                string[]? emailRecipients = Configuration.EmailRecipients;

                if (emailCredentials.HasValue && emailRecipients is not null && emailRecipients.Length > 0)
                {
                    using (
                            SmtpClient client = new()
                            {
                                Host = "smtp-mail.outlook.com",
                                Port = 587,
                                DeliveryMethod = SmtpDeliveryMethod.Network,
                                UseDefaultCredentials = false,
                                EnableSsl = true,
                                Credentials = new NetworkCredential(emailCredentials.Value.Key, emailCredentials.Value.Value)
                            })
                    {
                        MailAddress ma = new(Configuration.EmailAddress ?? emailCredentials.Value.Key, "SAMe Validation & Importer");
                        using (MailMessage mail = new())
                        {
                            mail.From = ma;
                            foreach (string recipient in emailRecipients)
                            {
                                mail.To.Add(recipient);
                            }
                            mail.Subject = "Run Report";
                            mail.Body = GetCapturedText();
                            await client.SendMailAsync(mail);
                            emailSent = true;
                        }
                    }
                }
            }
            catch { emailSent = false; }
            try
            {
                string? reportDir = Configuration.RunReportDir;

                if (reportDir is not null)
                {
                    if (Path.Exists(reportDir))
                    {
                        Directory.CreateDirectory(reportDir);
                    }

                    using (StreamWriter writer = new(Path.Combine(reportDir, $"RunReport{DateTime.Now:yyyyMMddTHHmmss}.txt"), true))
                    {
                        await writer.WriteAsync(GetCapturedText());
                        fileWritten = true;
                    }
                }
            }
            catch { fileWritten = false; }

            return emailSent || fileWritten;
        }
    }
}
