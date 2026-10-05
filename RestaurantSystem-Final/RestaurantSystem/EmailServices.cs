using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace RestaurantSystem
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string toEmail, string resetLink);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        // Sends the password reset link to whatever email address the user typed in
        // (i.e. the recipient is dynamic — the Gmail account below is only used as
        // the "sender" identity to authenticate with Gmail's SMTP server).
        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var smtpServer = _config["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"] ?? "587");
            var senderEmail = _config["EmailSettings:SenderEmail"] ?? "";
            var senderPassword = _config["EmailSettings:SenderPassword"] ?? "";
            var senderName = _config["EmailSettings:SenderName"] ?? "BITE Restaurant";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Reset your BITE Restaurant password";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family: Segoe UI, sans-serif; max-width: 500px; margin: auto; color:#333;'>
                        <h2 style='margin-bottom: 4px;'>
                            <span style='color:#ff9f43;'>BITE</span>
                            <span style='color:#1a1a1a;'>Restaurant</span>
                        </h2>
                        <p>We received a request to reset your password.</p>
                        <p>Click the button below to set a new password. This link will expire in 30 minutes.</p>
                        <p style='text-align:center; margin: 30px 0;'>
                            <a href='{resetLink}'
                               style='background:#ff9f43; color:#ffffff; padding:12px 28px; border-radius:30px; text-decoration:none; font-weight:bold; display:inline-block;'>
                               Reset Password
                            </a>
                        </p>
                        <p style='font-size:12px; color:#888;'>
                            If you didn't request this, you can safely ignore this email — your password will not be changed.
                        </p>
                    </div>",
                TextBody = $"We received a request to reset your password.\n\n" +
                           $"Open this link to set a new password (expires in 30 minutes):\n{resetLink}\n\n" +
                           $"If you didn't request this, you can safely ignore this email."
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(senderEmail, senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}