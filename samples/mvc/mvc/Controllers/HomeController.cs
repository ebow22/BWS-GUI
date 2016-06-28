﻿using mvc.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace mvc.Controllers
{
    public class HomeController : Controller
    {
        // TODO: fill in your BWS Application-ID and -secret:
        const string _appID = "595520272.6.app.bioid.com";
        const string _appSecret = "nGRoOZ5xKsdSWkiwa/c5klxE";

        public ActionResult Index()
        {
            return View(new PerformTaskModel());
        }

        [HttpPost]
        public async Task<ActionResult> Verify(PerformTaskModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(Index), model);
            }
            return await PerformTask(model, "verify");
        }

        [HttpPost]
        public async Task<ActionResult> Enroll(PerformTaskModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(Index), model);
            }
            return await PerformTask(model, "enroll");
        }

        private async Task<ActionResult> PerformTask(PerformTaskModel model, string task)
        {
            // well lets start by fetching a BWS token
            HttpClient httpClient = new HttpClient();
            string credentials = Convert.ToBase64String(System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes($"{_appID}:{_appSecret}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            string query = $"?id={_appID}&bcid={model.BCID}&task={task}&livedetection=true&challenge={model.ChallengeResponse}&autoenroll={model.AutoEnroll}";
            UriBuilder uri = new UriBuilder("https", model.Host, 443, "extension/token", query);
            var response = await httpClient.GetAsync(uri.Uri);
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", $"Token Request returned '{response.StatusCode}': {response.Content.ReadAsStringAsync().Result}");
                return View(nameof(Index), model);
            }

            // fine, fine, lets read the token
            var token = await response.Content.ReadAsStringAsync();

            // parse the token to find additional settings for the user interface
            var claimstring = System.Text.Encoding.UTF8.GetString(Utils.Base64Url.Decode(token.Split('.')[1]));
            var claims = JObject.Parse(claimstring);
            var taskFlags = (TokenTask)claims["task"].Value<int>();
            var challengesJson = model.ChallengeResponse && claims["challenge"] != null ? claims["challenge"].Value<string>() : "[]";

            // render the BWS unified user interface
            return View("PerformTask", new UUIViewModel
            {
                Task = (taskFlags & TokenTask.Enroll) == TokenTask.Enroll ? "enrollment" : "verification",
                MaxTries = (int)(taskFlags & TokenTask.MaxTriesMask),
                ChallengeResponse = (taskFlags & TokenTask.ChallengeResponse) == TokenTask.ChallengeResponse,
                ChallengesJson = challengesJson,
                Token = token,
                Host = model.Host,
                ReturnUrl = new Uri(Request.Url, "Callback").ToString(),
                State = "encrypted_app_status",
                AutoEnroll = (taskFlags & TokenTask.AutoEnroll) == TokenTask.AutoEnroll,
                AutoStart = false
            });
        }

        // Flags as used in the BWS token to identify the tasks that need to be performed with this token.
        [Flags]
        public enum TokenTask
        {
            Verify = 0,
            Enroll = 0x20,
            MaxTriesMask = 0x0F,
            LiveDetection = 0x100,
            ChallengeResponse = 0x200,
            AutoEnroll = 0x1000
        }

        public async Task<ActionResult> Callback(string access_token, string error, string state)
        {
            if (string.IsNullOrEmpty(access_token))
            {
                // redirect to an error page
                throw new ApplicationException("BWS callback has been invoked with invalid arguments.");
            }

            var model = new PerformTaskModel();
            // decode the token:
            var claimstring = System.Text.Encoding.UTF8.GetString(Utils.Base64Url.Decode(access_token.Split('.')[1]));
            var claims = JObject.Parse(claimstring);
            model.BCID = (string)claims["bcid"];
            Uri host = new Uri((string)claims["aud"]);
            model.Host = host.Host;

            if (!string.IsNullOrWhiteSpace(error))
            {
                model.Error = error;
            }
            // ask the BWS for the result
            try
            {
                HttpClient httpClient = new HttpClient();
                string credentials = Convert.ToBase64String(System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes($"{_appID}:{_appSecret}"));
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                UriBuilder uri = new UriBuilder("https", model.Host, 443, "extension/result", $"?access_token={access_token}");
                var response = await httpClient.GetAsync(uri.Uri);
                string content = await response.Content.ReadAsStringAsync();
                model.Result = $"{response.StatusCode} ({(int)response.StatusCode}): {content}";
            }
            catch (Exception ex)
            {
                model.Result = $"Error: {ex.Message}";
            }
            return View(nameof(Index), model);
        }
    }
}