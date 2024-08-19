// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.
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
        public async void EmailSend(SmtpConfig config, string subject, string body, string to = null)
        {
            var mailTo = config.To;
            if (!string.IsNullOrWhiteSpace(to))
            {
                mailTo = to;
            }

            if (string.IsNullOrWhiteSpace(config?.FromPassword) || string.IsNullOrWhiteSpace(mailTo))
            {
                return;
            }

            try
            {
                // SMTP服务器信息
                string smtpServer = config.Host; // "smtp.mxhichina.com"; // 请替换为你的SMTP服务器地址
                int port = config.Port; // SMTP端口，一般为587或465，具体依据你的SMTP服务器而定
                bool enableSsl = config.EnableSsl; // 根据你的SMTP服务器要求设置

                // 邮件账户信息
                string userName = config.FromEmail; // 你的邮箱地址
                string password = config.FromPassword; // 你的邮箱密码或应用专用密码

                string fromName = config.FromName; // 发件人昵称
                string fromEmail = config.FromEmail; // 发件人邮箱地址

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
                Log.Error(ex, "发送邮件失败");

                try
                {
                    // 尝试第二次
                    await EmailSender.Instance.SendEmailAsync(config, mailTo, subject, body);
                }
                catch (Exception exx)
                {
                    Log.Error(exx, "第二次发送邮件失败");
                }
            }
        }
    }
}