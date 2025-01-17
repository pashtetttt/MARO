﻿using MARO.Application.Aggregate.Models.DTOs;
using MARO.Application.Aggregate.Models.ResponseModels;
using MARO.Application.Common.Exceptions;
using MARO.Application.Interfaces;
using MARO.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Encodings.Web;

namespace MARO.Application.Repository.AuthRepo
{
    public class AuthRepository : IAuthRepository
    {
        private readonly UserManager<User> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly IMARODbContext _dbContext;
        private readonly ILogger<AuthRepository> _logger;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AuthRepository(UserManager<User> userManager, IEmailSender emailSender, ISmsSender smsSender, IMARODbContext dbContext, ILogger<AuthRepository> logger, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _dbContext = dbContext;
            _logger = logger;
            _roleManager = roleManager;
        }

        public async Task<ConfirmResponseModel> EmailConfirm(string userId, string code, string? returnUrl)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) throw new NotFoundException(nameof(User), userId);

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));

            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (result.Succeeded)
            {
                return new ConfirmResponseModel
                {
                    Message = "Email подтверждён",
                    ReturnUrl = returnUrl
                };
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    throw new Exception(error.Description);
                }
            }

            throw new Exception("Ошибка подтверждения Email");
        }

        public async Task EmailForgotPassword(string email, string UrlRaw)
        {
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null) throw new ArgumentException("Пользователь не найден или аккаунт не подтверждён");

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = $"{UrlRaw}/reset-password?email={email}&code={code}";

            _emailSender.Subject = "Сброс пароля";
            _emailSender.To = user.Email;

            _emailSender.Message = $"Для сброса пароля перейдите по <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>этой ссылке</a>.";

            await _emailSender.SendAsync();
        }

        public async Task<RegisterResponseModel> EmailRegister(string arg, string password, string UrlRaw, string returnUrl)
        {
            var user = new User
            {
                UserName = arg,
                Email = arg
            };

            var getUser = await _userManager.FindByEmailAsync(arg);

            if (getUser is not null) throw new Exception($"Пользователь с таким Email уже существует");

            var result = await _userManager.CreateAsync(user, password);

            if (result.Succeeded)
            {
                _logger.LogInformation($"UserID: {user.Id}. Пользователь создал новую учетную запись с паролем");

                var role = await _roleManager.FindByNameAsync("user");

                if (role == null) throw new NotFoundException(nameof(IdentityRole), "user");

                await _userManager.AddToRoleAsync(user, role.Name);

                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = $"{UrlRaw}/confirm?userId={userId}&code={code}&returnUrl={returnUrl}";

                _emailSender.Subject = "Подтвердите Вашу почту";
                _emailSender.To = arg;
                _emailSender.Message = $"Для подтверждения аккаунта перейдите по <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>этой ссылке</a>.";

                await _emailSender.SendAsync();

                return new RegisterResponseModel { UserId = userId, ReturnUrl = returnUrl! };
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    throw new Exception(error.Description);
                }
            }

            throw new Exception("Ошибка регистрации по Email");
        }

        public async Task<string> LoginAsGuest()
        {
            var id = Guid.NewGuid().ToString();

            var user = new User
            {
                Id = id,
                UserName = id
            };

            var result = await _userManager.CreateAsync(user);

            if (result.Succeeded)
            {

                var role = await _roleManager.FindByNameAsync("guest");

                if (role == null) throw new NotFoundException(nameof(IdentityRole), "guest");

                await _userManager.AddToRoleAsync(user, role.Name);
                await _userManager.UpdateAsync(user);

                return user.Id;
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    throw new Exception(error.Description);
                }
            }

            throw new Exception("Ошибка выдачи id гостю");
        }

        public async Task<ConfirmResponseModel> PhoneConfirm(string userId, string code, string? returnUrl)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null) throw new NotFoundException(nameof(User), userId);

            if (user.PhoneConfirmationCode == code)
            {
                user.PhoneConfirmationCode = null;
                user.PhoneNumberConfirmed = true;

                await _userManager.UpdateAsync(user);

                return new ConfirmResponseModel
                {
                    Message = "Номер телефона подтверждён",
                    ReturnUrl = returnUrl
                };
            }
            else throw new Exception("Ошибка подтверждения номера телефона");
        }

        public async Task PhoneForgotPassword(string phoneNumber)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);

            if (user == null || !await _userManager.IsPhoneNumberConfirmedAsync(user)) throw new ArgumentException("Пользователь не найден или аккаунт не подтверждён");

            var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);

            user.PhoneConfirmationCode = code;

            _dbContext.Users.Update(user);

            await _dbContext.SaveChangesAsync(CancellationToken.None);

            _smsSender.Message = $"Ваш код для сброса: {code}";

            await _smsSender.SendAsync();
        }

        public async Task<string[]> PhoneRegister(string arg, string password, string returnUrl)
        {
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                UserName = arg,
                PhoneNumber = arg
            };

            var getUser = await _userManager.FindByNameAsync(arg);

            if (getUser is not null) throw new Exception("Пользователь с таким номером телефона уже зарегистрирован");

            var result = await _userManager.CreateAsync(user, password);

            var role = await _roleManager.FindByNameAsync("user");

            if (role == null) throw new NotFoundException(nameof(IdentityRole), null!);

            await _userManager.AddToRoleAsync(user, role.Name);

            if (result.Succeeded)
            {
                _logger.LogInformation($"UserID: {user.Id}. Пользователь создал новую учетную запись с паролем");

                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber);

                user.PhoneConfirmationCode = code;

                await _userManager.UpdateAsync(user);

                _smsSender.Message = $"Ваш код: {code}";

                await _smsSender.SendAsync();

                return new string[] { user.Id, returnUrl };
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    throw new Exception(error.Description);
                }
            }

            throw new Exception("Ошибка регистрации по номеру телефона");
        }

        public async Task ResetPassword(ResetPasswordViewModel model)
        {
            var user = await _userManager.FindByNameAsync(model.Arg);

            if (user == null) throw new NotFoundException(nameof(User), model.Arg);

            IdentityResult? result = null;

            if (model.Code.Length != 6)
            {
                var encodeCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
                result = await _userManager.ResetPasswordAsync(user, encodeCode, model.Password);
            }
            if (model.Code.Length == 6)
            {
                if (model.Code != user.PhoneConfirmationCode) result = IdentityResult.Failed(new IdentityError { Description = "Неверный код подтверждения" });

                user.PhoneConfirmationCode = null;
                _userManager.PasswordHasher.HashPassword(user, model.Password);
                await _userManager.UpdateAsync(user);

                result = IdentityResult.Success;
            }

            if (result!.Succeeded)
            {
                return;
            }
            foreach (var error in result.Errors)
            {
                throw new Exception(error.Description);
            }

            throw new Exception("Ошибка сброса пароля");
        }
    }
}
