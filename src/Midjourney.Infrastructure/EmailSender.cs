using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using Serilog;

using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Midjourney.Infrastructure
{
    public class EmailSender : SingletonBase<EmailSender>
    {
        /// <summary>
        /// 发送 2
        /// </summary>
        /// <param name="config"></param>
        /// <param name="email"></param>
        /// <param name="subject"></param>
        /// <param name="body"></param>
        /// <param name="isHtml"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SendEmailAsync(SmtpConfig config, string email, string subject, string body, bool isHtml = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config?.FromPassword) || string.IsNullOrWhiteSpace(config?.To))
                {
                    return;
                }

                var smtpUserName = config.FromName;
                var smtpHost = config.Host;
                var smtpPort = config.Port;
                var smtpPassword = config.FromPassword;

                if (string.IsNullOrWhiteSpace(smtpUserName))
                    throw new ArgumentNullException(nameof(smtpUserName));
                if (string.IsNullOrWhiteSpace(smtpPassword))
                    throw new ArgumentNullException(nameof(smtpPassword));

                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(smtpUserName));
                message.To.Add(MailboxAddress.Parse(email));
                message.Subject = subject;

                var textFormat = isHtml ? TextFormat.Html : TextFormat.Plain;
                message.Body = new TextPart(textFormat)
                {
                    Text = body
                };

                using (var client = new SmtpClient())
                {
                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                    await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);

                    client.AuthenticationMechanisms.Remove("XOAUTH2");

                    await client.AuthenticateAsync(smtpUserName, smtpPassword);

                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "邮件发送异常");
            }
        }

        /// <summary>
        /// 465 发送
        /// </summary>
        /// <param name="smtpserver"></param>
        /// <param name="userName"></param>
        /// <param name="pwd"></param>
        /// <param name="nickName"></param>
        /// <param name="strfrom"></param>
        /// <param name="strto"></param>
        /// <param name="messageSubject"></param>
        /// <param name="messageBody"></param>
        /// <param name="port"></param>
        /// <param name="enableSsl"></param>
        /// <returns></returns>
        public static void SendMimeKitEmail(string smtpserver,
            string userName,
            string pwd,
            string nickName,
            string strfrom,
            string strto,
            string messageSubject,
            string messageBody,
            int port, bool enableSsl)
        {
            var emailMessage = new MimeMessage();

            emailMessage.From.Add(new MailboxAddress(nickName, strfrom));
            emailMessage.To.Add(new MailboxAddress("", strto));
            emailMessage.Subject = messageSubject;

            var bodyBuilder = new BodyBuilder { HtmlBody = messageBody };

            //// 附件处理
            //if (!string.IsNullOrEmpty(supFile))
            //{
            //    // 请确保supFile是文件的完整路径
            //    bodyBuilder.Attachments.Add(supFile);
            //}

            emailMessage.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                try
                {
                    client.Connect(smtpserver, port, enableSsl);

                    // 注意: 只有当SMTP服务器需要身份验证时，才需要调用Authenticate方法
                    client.Authenticate(userName, pwd);

                    client.Send(emailMessage);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "邮件发送异常");
                }
                finally
                {
                    client.Disconnect(true);
                    client.Dispose();
                }
            }
        }
    }
}