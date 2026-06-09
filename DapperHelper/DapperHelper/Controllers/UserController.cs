using BLL.Services;
using Microsoft.AspNetCore.Mvc;
using Models;

namespace DapperHelper.Controllers;

[ApiController]
[Route("user")]
public class UserController(IUserService userService) : Controller
{

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await userService.GetAllAsync();
        return Ok(result);
    }
    
    [HttpPost("/custom")]
    public async Task<IActionResult> GetByUserIdA(CustomDto req)
    {
        var result = await userService.GetUserByIdAsync(req.Id);
        return result is null ? NotFound() : Ok(result);
    }
    
    [HttpPost]
    public async Task<IActionResult> GetByUserId(AuthRequest authRequest)
    {
        var result = await userService.GetUserByCredentialsAsync(authRequest.Email, authRequest.Password);
        return result is null ? NotFound() : Ok(result);
    }
    
    public record AuthRequest(string Email, string Password);
    public record CustomDto(int Id);
}