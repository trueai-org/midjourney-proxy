﻿// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
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

using System.Diagnostics;
using Midjourney.Infrastructure.Services;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Serilog;

namespace Midjourney.Captcha.API
{
    /// <summary>
    /// Selenium 登录助手
    /// </summary>
    public class SeleniumLoginHelper
    {
        /// <summary>
        /// 登录
        /// </summary>
        /// <param name="contentRootPath"> </param>
        /// <param name="clientKey"></param>
        /// <param name="loginAccount"></param>
        /// <param name="loginPassword"></param>
        /// <param name="twofa"></param>
        public static string Login(string contentRootPath, string clientKey, string loginAccount, string loginPassword, string twofa)
        {
            ChromeDriver driver = null;
            try
            {
                // 插件版本
                var pluVersion = "google_pro_1.1.64";

                var configPath = Path.Combine(contentRootPath, "Extensions", pluVersion, "config.js");
                var configContent = "";
                if (File.Exists(configPath))
                {
                    configContent = File.ReadAllText(configPath);
                }

                var configDemoPath = Path.Combine(contentRootPath, "Extensions", pluVersion, "config-demo.js");
                var configDemoContent = File.ReadAllText(configDemoPath)
                    .Replace("$clientKey", clientKey);

                // 如果不一样，就写入
                if (configDemoContent != configContent)
                {
                    File.WriteAllText(configPath, configDemoContent);
                }

                driver = GetChrome(false, false, contentRootPath, pluVersion);
                driver.Navigate().GoToUrl("https://discord.com/login");

                Thread.Sleep(5000);

                // 判断是否加载网页完成
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
                wait.Until(d => d.FindElement(By.CssSelector("input[name='email']")));

                // 输入框
                // 查找 name = email 的 input
                var emailInput = driver.FindElement(By.Name("email"));
                emailInput.SendKeys(loginAccount);

                Thread.Sleep(1000);

                // 查找 name = password 的 input
                var passwordInput = driver.FindElement(By.Name("password"));
                passwordInput.SendKeys(loginPassword);

                Thread.Sleep(5000);

                // 查找 type = submit 的 button
                var submitButton = driver.FindElement(By.CssSelector("button[type='submit']"));
                submitButton.Click();

                Thread.Sleep(5000);

                var sw = new Stopwatch();
                sw.Start();

                while (true)
                {
                    // placeholder="6位数字身份验证码"
                    var codeInput = driver.FindElements(By.CssSelector("input[placeholder='6位数字身份验证码']"))
                        ?.FirstOrDefault();

                    // 如果找到了
                    if (codeInput != null)
                    {
                        while (true)
                        {
                            var num = TwoFAHelper.GenerateOtp(twofa);

                            // 清除
                            codeInput.Clear();
                            codeInput.SendKeys(num);

                            Thread.Sleep(1000);

                            // 查找 type = submit 的 button
                            var submitButton2 = driver.FindElements(By.CssSelector("button[type='submit']"))
                                .LastOrDefault();
                            submitButton2.Click();

                            // 从这里开始记录所有的网络请求
                            driver.Manage().Logs.GetLog(LogType.Browser);

                            Thread.Sleep(5000);

                            // <div>双重认证码无效</div>
                            // 查找 div 文本: 双重认证码无效
                            var error = driver.FindElements(By.XPath("//div[contains(text(),'双重认证码无效')]"))?.FirstOrDefault();
                            if (error == null)
                            {
                                // 等待并获取 token
                                Thread.Sleep(5000);

                                // 方式2
                                // 可以通过获取网络请求头上的 authorization 字段，来获取 token

                                var wait2 = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                                wait2.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete");

                                var sc = @"
window.webpackChunkdiscord_app.push([
  [Math.random()],
  {},
  (req) => {
    for (const m of Object.keys(req.c)
      .map((x) => req.c[x].exports)
      .filter((x) => x)) {
      if (m.default && m.default.getToken !== undefined) {
        const token = m.default.getToken();
        document.body.setAttribute('data-token', token);
      }
      if (m.getToken !== undefined) {
        const token = m.getToken();
        document.body.setAttribute('data-token', token);
      }
    }
  },
]);
";
                                // 通过控制台执行 sc 脚本获取 token
                                driver.ExecuteScript(sc);

                                Thread.Sleep(5000);

                                var token = driver.FindElement(By.CssSelector("body")).GetAttribute("data-token");
                                if (!string.IsNullOrEmpty(token))
                                {
                                    Log.Information($"token: {token}");

                                    return token;
                                }
                            }

                            if (sw.ElapsedMilliseconds > 120 * 1000)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (sw.ElapsedMilliseconds > 120 * 1000)
                        {
                            break;
                        }

                        Thread.Sleep(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "自动登录执行异常");
            }
            finally
            {
                // 关闭浏览器
                driver?.Quit();
            }

            return null;
        }

        /// <summary>
        /// 获取 chrome
        /// </summary>
        /// <param name="isHeadless"></param>
        /// <param name="isMobile"></param>
        /// <param name="contentRootPath"></param>
        /// <param name="pluVersion"></param>
        /// <returns></returns>
        private static ChromeDriver GetChrome(bool isHeadless, bool isMobile, string contentRootPath, string pluVersion)
        {
            //// 设置输出编码，否则可能浏览器乱码，在后台运行模式时
            //Console.OutputEncoding = System.Text.Encoding.UTF8;

            var options = new ChromeOptions();
            if (isHeadless)
            {
                options.AddArgument("headless"); // 可选，如果设置将在后台模式下运行测试
            }
            options.AddArgument("disable-gpu"); // 可选，如果设置将禁用GPU硬件加速
            options.AddArgument("lang=zh_CN.UTF-8"); // 可选，设置默认浏览器语言为zh_CN.UTF-8
            options.AddArgument("--log-level=3"); // 设置日志级别为3。有效值从0到3：INFO = 0，WARNING = 1，LOG_ERROR = 2，LOG_FATAL = 3。

            //options.AddArgument("--auto-open-devtools-for-tabs"); // 打开 DevTools（可选）

            if (isMobile)
            {
                // Create a dictionary to store the parameters of the mobile emulation
                // "iPhone X" "iPhone 6/7/8""iPhone 6/7/8 Plus""iPhone SE""iPad""iPad Pro""iPad Mini""Galaxy S5""Pixel 2""Pixel 2 XL""Nexus 6P""Nexus 5X"
                options.EnableMobileEmulation("iPhone X");
            }

            // 加载解压后的扩展
            var path = Path.Combine(contentRootPath, "Extensions", pluVersion);
            options.AddArgument($"--load-extension={path}");

            //options.AddArgument("--window-size=360,640");
            //options.AddArgument("--user-agent=Mozilla/5.0 (Linux; Android 10; Pixel 3) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.66 Mobile Safari/537.36");

            // 在使用Selenium WebDriver时，可以通过设置ChromeDriverService属性来隐藏其后台运行的控制台窗口。
            var driverService = ChromeDriverService.CreateDefaultService();

            if (isHeadless)
            {
                driverService.HideCommandPromptWindow = true;
            }

            // 创建ChromeDriver实例
            var driver = new ChromeDriver(driverService, options);

            return driver;
        }
    }
}