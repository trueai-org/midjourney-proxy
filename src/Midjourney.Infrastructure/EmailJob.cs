using Serilog;
using System.Net;
using System.Net.Mail;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 邮件发送通知
    /// https://help.aliyun.com/document_detail/29451.html?spm=a2c4g.11186623.6.607.383c2649OgIrok
    /// </summary>
    public class EmailJob : SingletonBase<EmailJob>
    {
        public async void EmailSend(SmtpConfig config, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(config?.FromPassword) || string.IsNullOrWhiteSpace(config?.To))
            {
                return;
            }

            var mailTo = config.To;

            // SMTP服务器信息
            string smtpServer = config.Host; // "smtp.mxhichina.com"; // 请替换为你的SMTP服务器地址
            int port = config.Port; // SMTP端口，一般为587或465，具体依据你的SMTP服务器而定
            bool enableSsl = config.EnableSsl; // 根据你的SMTP服务器要求设置

            // 邮件账户信息
            string userName = config.FromEmail; // 你的邮箱地址
            string password = config.FromPassword; // 你的邮箱密码或应用专用密码

            string fromName = config.FromName; // 发件人昵称
            string fromEmail = config.FromEmail; // 发件人邮箱地址

            try
            {
                // 调用邮件发送方法
                EmailSender.SendMimeKitEmail(smtpServer, userName, password, fromName, fromEmail, mailTo, subject, body, port, enableSsl);

                // 不处理
                return;

                var mailFrom = fromEmail;

                MailMessage mymail = new MailMessage();
                mymail.From = new MailAddress(mailFrom);
                mymail.To.Add(mailTo);
                mymail.Subject = subject;
                mymail.Body = body;
                //mymail.IsBodyHtml = true;
                //mymail.Attachments.Add(new Attachment(path));

                using (SmtpClient smtpclient = new SmtpClient())
                {
                    smtpclient.Port = 587;
                    smtpclient.UseDefaultCredentials = false;
                    smtpclient.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtpclient.Host = smtpServer;
                    smtpclient.EnableSsl = true;
                    smtpclient.Credentials = new NetworkCredential(fromEmail, password);
                    smtpclient.Send(mymail);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"发送邮件失败");

                // 尝试第二次
                await EmailSender.Instance.SendEmailAsync(config, mailTo, subject, body);
            }
        }
    }
}