using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GenAi.Web.Controllers;

[AllowAnonymous]
[Controller]
public class LoginController(
    IConfiguration config
    ) : Controller
{
    [HttpGet("/login")]
    public IActionResult Index(string returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (User?.Identity?.IsAuthenticated == true)
        {
            return Redirect("~/");
        }

        return View();
    }

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string password, string returnUrl)
    {
        if (password == config["Passphrase"])
        {
            var identity = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "User")
                ],
                CookieAuthenticationDefaults.AuthenticationScheme));
            await HttpContext.SignInAsync(identity);

            return Redirect(returnUrl);
        } else
        {
            return Forbid();
        }
    }
}
