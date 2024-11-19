using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Services;

namespace Midjourney.Captcha.API.Controllers
{
    /// <summary>
    /// 2FA
    /// </summary>
    [ApiController]
    [Route("/")]
    public class TwoFAController : Controller
    {
        [HttpGet("{secret}")]
        public IActionResult GetOtp(string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                return BadRequest("Missing secret parameter");
            }

            var loadTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var otp = TwoFAHelper.GenerateOtp(secret, loadTime);

            var remainingTime = TwoFAHelper.CalculateRemainingTime(loadTime);

            var htmlContent = $@"
<html>
  <head>
    <title>2FA Auth</title>
    <script>
      const loadTime = {loadTime};
      const remainingTime = {remainingTime};

      if (remainingTime <= 0) {{
        location.reload();
      }} else {{
        setTimeout(() => {{
          location.reload();
        }}, remainingTime * 1000);
      }}
    </script>
  </head>
  <body>
    <pre>{{
  ""token"": ""{otp}""
}}</pre>
  </body>
</html>
";
            return Content(htmlContent, "text/html");
        }
    }
}