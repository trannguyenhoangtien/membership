﻿using Membership.Data.Entities;
using Membership.Service.interfaces;
using Membership.ViewModel.Common;
using Membership.ViewModel.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Membership.Service
{
    public class UserService : IUserService
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly RoleManager<AppRole> _roleManager;
        private readonly IConfiguration _config;
        public UserService(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager
            , RoleManager<AppRole> roleManager, IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _config = config;
        }
        public async Task<ResponseResult<UserAuthenticateVm>> Authenticate(LoginRequest request)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(request.UserName);
                if (user == null) return new ResponseErrorResult<UserAuthenticateVm>("Username or Password invalid.");

                var result = await _signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, true);
                if (!result.Succeeded) return new ResponseErrorResult<UserAuthenticateVm>("Username or Password invalid.");

                var roles = await _userManager.GetRolesAsync(user);
                var claims = new[]
                {
                    new Claim("id", user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.GivenName, user.FirstName),
                    new Claim(ClaimTypes.Role, string.Join(";", roles)),
                    new Claim(ClaimTypes.Name, string.Join(";", user.UserName)),
                };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Tokens:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    _config["Tokens:Issuer"],
                    _config["Tokens:Issuer"],
                    claims,
                    expires: DateTime.Now.AddHours(3),
                    signingCredentials: creds);

                return new ResponseSuccessResult<UserAuthenticateVm>(new UserAuthenticateVm() { 
                    Token = new JwtSecurityTokenHandler().WriteToken(token),
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Username = user.UserName,
                });
            }
            catch (Exception ex)
            {
                return new ResponseErrorResult<UserAuthenticateVm>(ex.Message);
            }
        }

        public async Task<ResponseResult<bool>> Delete(Guid id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id.ToString());
                if (user == null) return new ResponseErrorResult<bool>("User not exist");

                var result = await _userManager.DeleteAsync(user);
                if (result.Succeeded) return new ResponseSuccessResult<bool>();

                return new ResponseErrorResult<bool>("Delete Fail");
            }
            catch (Exception ex)
            {
                return new ResponseErrorResult<bool>(ex.Message);
            }
        }

        public async Task<ResponseResult<UserVm>> GetById(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return new ResponseErrorResult<UserVm>("User not exist");

            var roles = await _userManager.GetRolesAsync(user);
            var userVm = new UserVm()
            {
                DOB = user.DOB,
                Email = user.Email,
                FirstName = user.FirstName,
                Id = user.Id,
                LastName = user.LastName,
                Phone = user.PhoneNumber,
                Roles = roles,
                UserName = user.UserName
            };

            return new ResponseSuccessResult<UserVm>(userVm);
        }

        public async Task<ResponseResult<PagedResult<UserVm>>> GetUserPaging(GetUserPagingRequest request)
        {
            var query = _userManager.Users.Where(x => request.Keyword == null 
                || x.UserName.Contains(request.Keyword)
                || x.PhoneNumber.Contains(request.Keyword));

            int totalRow = await query.CountAsync();

            var data = await query.Skip((request.PageIndex - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(x => new UserVm()
                {
                    DOB = x.DOB,
                    Email = x.Email,
                    FirstName = x.FirstName,
                    Id = x.Id,
                    LastName = x.LastName,
                    Phone = x.PhoneNumber,
                    UserName = x.UserName
                }).ToListAsync();

            var pagedResult = new PagedResult<UserVm>()
            {
                Items = data,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize,
                TotalRecords = totalRow
            };

            return new ResponseSuccessResult<PagedResult<UserVm>>(pagedResult);
        }

        public async Task<ResponseResult<bool>> Register(RegisterRequest request)
        {
            if (await _userManager.FindByNameAsync(request.UserName) != null)
                return new ResponseErrorResult<bool>("Username already exist");

            if (await _userManager.FindByEmailAsync(request.Email) != null)
                return new ResponseErrorResult<bool>("Email already exist");

            var user = new AppUser()
            {
                DOB = request.DOB,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                UserName = request.UserName,
                PhoneNumber = request.PhoneNumber
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return new ResponseErrorResult<bool>(result.Errors.ToString());

            return new ResponseSuccessResult<bool>();
        }

        public async Task<ResponseResult<bool>> RoleAssign(RoleAssignRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.Id.ToString());
            if (user == null) return new ResponseErrorResult<bool>("User not exist");

            var removeRoles = request.Roles.Where(x => x.Selected == false).Select(x => x.Name).ToList();
            await _userManager.RemoveFromRolesAsync(user, removeRoles);
            //foreach (var roleName in removeRoles)
            //{
            //    if (await _userManager.IsInRoleAsync(user, roleName))
            //        await _userManager.RemoveFromRoleAsync(user, roleName);
            //}

            var addRoles = request.Roles.Where(x => x.Selected == true).Select(x => x.Name).ToList();
            await _userManager.AddToRolesAsync(user, addRoles);

            return new ResponseSuccessResult<bool>();
        }

        public async Task<ResponseResult<bool>> Update(UserUpdateRequest request)
        {
            if (await _userManager.Users.AnyAsync(x => x.Email == request.Email && x.Id == request.Id))
                return new ResponseErrorResult<bool>("Email alredy exist");

            var user = await _userManager.FindByIdAsync(request.Id.ToString());
            user.DOB = request.DOB;
            user.Email = request.Email;
            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.PhoneNumber = request.PhoneNumber;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded) return new ResponseErrorResult<bool>(result.Errors.ToString());

            return new ResponseSuccessResult<bool>();

        }
    }
}
