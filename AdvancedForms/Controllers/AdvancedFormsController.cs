﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display;
using OrchardCore.DisplayManagement.ModelBinding;
using System.Threading.Tasks;
using AdvancedForms.ViewModels;
using Newtonsoft.Json.Linq;
using AdvancedForms.Models;
using Microsoft.AspNetCore.Http;
using AdvancedForms.Enums;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.DisplayManagement.Notify;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.ContentManagement.Records;
using YesSql;
using OrchardCore.Mvc.ActionConstraints;

namespace AdvancedForms.Controllers
{
    [Authorize]
    public class AdvancedFormsController : Controller, IUpdateModel
    {

        private readonly IContentManager _contentManager;
        private readonly IContentItemDisplayManager _contentItemDisplayManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly IContentAliasManager _contentAliasManager;
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly INotifier _notifier;
        private readonly YesSql.ISession _session;
        private const string _id = "AdvancedFormSubmissions";

        public AdvancedFormsController(
            IContentManager contentManager,
            IContentItemDisplayManager contentItemDisplayManager,
            IAuthorizationService authorizationService,
            IContentAliasManager contentAliasManager,
            INotifier notifier,
            YesSql.ISession session,
            IContentDefinitionManager contentDefinitionManager,
            IHtmlLocalizer<AdvancedFormsController> localizer
            )
        {
            _authorizationService = authorizationService;
            _contentItemDisplayManager = contentItemDisplayManager;
            _contentManager = contentManager;
            _notifier = notifier;
            _contentAliasManager = contentAliasManager;
            _session = session;
            _contentDefinitionManager = contentDefinitionManager;
            T = localizer;
        }

        public IHtmlLocalizer T { get; }

        [Route("AdvancedForms/{alias}")]
        public async Task<IActionResult> Display(string alias)
        {
            if (String.IsNullOrWhiteSpace(alias))
            {
                return Redirect("/");
            }

            var contentItemId = await _contentAliasManager.GetContentItemIdAsync("slug:AdvancedForms/" + alias);

            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Published);

            if (contentItem == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewContent, contentItem))
            {
                return Unauthorized();
            }

            var model = new AdvancedFormViewModel
            {
                Id = contentItemId,
                Title = contentItem.DisplayText,
                Type = contentItem.Content.AdvancedForm.Type.Text,
                Container = contentItem.Content.AdvancedForm.Container.Html,
                AdminContainer = contentItem.Content.AdvancedForm.AdminContainer.Html,
                Description = contentItem.Content.AdvancedForm.Description.Html,
                Instructions = contentItem.Content.AdvancedForm.Instructions.Html,
                Header = contentItem.Content.AdvancedForm.Header.Html,
                Footer = contentItem.Content.AdvancedForm.Footer.Html
            };
            return View(model);
        }

        [HttpPost]
        [Route("AdvancedForms/SaveUpdateAdminComment")]
        public async Task<IActionResult> SaveAdminComment(string id, string contentItemId, string comment)
        {
            ContentItem content;
            if (!string.IsNullOrWhiteSpace(contentItemId))
            {
                content = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);
            }
            else
            {
                content = await _contentManager.NewAsync("AdminComment");
                await _contentManager.CreateAsync(content, VersionOptions.Draft);
            }

            var model = new CommentPart(comment);


            await _contentManager.PublishAsync(content);

            //return Ok(StatusCodes.Status200OK);
            return await EditCommentPOST(content.ContentItemId, false, id, model, async contentItem =>
            {
                await _contentManager.PublishAsync(contentItem);
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? T["Your content has been published."]
                    : T["Your {0} has been published.", typeDefinition.DisplayName]);
            });
        }



        [HttpPost]
        [Route("AdvancedForms/SaveUpdatePublicComment")]
        public async Task<IActionResult> SavePublicComment(string id, string contentItemId, string comment)
        {
            ContentItem content;
            if (!string.IsNullOrWhiteSpace(contentItemId))
            {
                content = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);
            }
            else
            {
                content = await _contentManager.NewAsync("PublicComment");
                await _contentManager.CreateAsync(content, VersionOptions.Draft);
            }

            var model = new CommentPart(comment);


            await _contentManager.PublishAsync(content);

            //return Ok(StatusCodes.Status200OK);
            return await EditCommentPOST(content.ContentItemId, true, id, model, async contentItem =>
            {
                await _contentManager.PublishAsync(contentItem);
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? T["Your content has been published."]
                    : T["Your {0} has been published.", typeDefinition.DisplayName]);
            });
        }

        private async Task<IActionResult> EditCommentPOST(string contentItemId, bool isPublic, string id, CommentPart viewModel, Func<ContentItem, Task> conditionallyPublish)
        {

            var content = await _contentManager.GetAsync(contentItemId, VersionOptions.DraftRequired);

            if (content == null)
            {
                return NotFound();
            }
            if (isPublic)
                content.Content.PublicComment = JToken.FromObject(viewModel);
            else
                content.Content.AdminComment = JToken.FromObject(viewModel);
            content.Owner = User.Identity.Name;
            content.DisplayText = id;

            await conditionallyPublish(content);

            _session.Save(content);

            var typeDefinition = _contentDefinitionManager.GetTypeDefinition(content.ContentType);
            return StatusCode(StatusCodes.Status201Created);
        }

        [HttpGet]
        [Route("AdvancedForms/GetPublicComments")]
        public async Task<IActionResult> GetPublicComments(string id)
        {
            var query = _session.Query<ContentItem, ContentItemIndex>();
            var comments = await query.Where(o => o.ContentType == "PublicComment" && o.DisplayText == id && (o.Latest || o.Published)).ListAsync();
            return Ok(comments);
        }

        [HttpPost]
        [Route("AdvancedForms/Entry")]
        public async Task<IActionResult> Entry(string submission, string title, string id, string container,
            string header, string footer, string description, string type, string submissionId, string instructions, string owner, string adminContainer, string adminSubmission, bool isDraft)
        {
            ContentItem content;
            if (!string.IsNullOrWhiteSpace(submissionId))
            {
                content = await _contentManager.GetAsync(submissionId, VersionOptions.Latest);
            }
            else
            {
                content = await _contentManager.NewAsync(_id);
                await _contentManager.CreateAsync(content, VersionOptions.Draft);
            }

            string metadata = string.Empty, data, status = string.Empty;
            var query = _session.Query<ContentItem, ContentItemIndex>();
            if (isDraft)
            {
                data = submission;
                var pageOfContentItems = await query.Where(o => o.ContentType == "AdvancedFormStatus" && o.DisplayText == "Draft" && (o.Latest || o.Published)).ListAsync();
                foreach (var item in pageOfContentItems)
                {
                    status = item.ContentItemId;
                }
            }
            else
            {
                var subObject = JObject.Parse(submission);
                metadata = subObject["metadata"].ToString();
                data = subObject["data"].ToString();
                var pageOfContentItems = await query.Where(o => o.ContentType == "AdvancedFormStatus" && o.DisplayText == "Submitted" && (o.Latest || o.Published)).ListAsync();
                foreach (var item in pageOfContentItems)
                {
                    status = item.ContentItemId;
                }
            }

            if (string.IsNullOrWhiteSpace(owner))
            {
                owner = User.Identity.Name;
            }
            string guid = content.ContentItemId;
            string subTitle = title + " " + DateTime.Now.ToUniversalTime().ToString() + " " + guid;

            var viewModel = new AdvancedFormSubmissions(data, metadata, subTitle, container, header, footer, description,
                type, instructions, owner, status, adminContainer, adminSubmission);

            return await EditPOST(content.ContentItemId, title, viewModel, async contentItem =>
            {
                if (!isDraft)
                {
                    await _contentManager.PublishAsync(contentItem);
                }

                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? T["Your content has been published."]
                    : T["Your {0} has been published.", typeDefinition.DisplayName]);
            });
        }

        [HttpPost]
        [Route("AdvancedForms/admin/{alias}/Submission/{id}")]
        [FormValueRequired("submit.Save")]
        public async Task<IActionResult> SubmissionSave(AdvancedFormViewModel model, string returnUrl = "")
        {
            ContentItem content;
            if (!string.IsNullOrWhiteSpace(model.SubmissionId))
            {
                content = await _contentManager.GetAsync(model.SubmissionId, VersionOptions.Latest);
            }
            else
            {
                content = await _contentManager.NewAsync(_id);
                await _contentManager.CreateAsync(content, VersionOptions.Draft);
            }

            if (string.IsNullOrWhiteSpace(model.Owner))
            {
                model.Owner = User.Identity.Name;
            }

            string guid = content.ContentItemId;
            string subTitle = model.Title + " " + DateTime.Now.ToUniversalTime().ToString() + " " + guid;
            var subObject = JObject.Parse(model.Submission);
            var viewModel = new AdvancedFormSubmissions(model.Submission,
            model.Metadata, subTitle, model.Container, model.Header, model.Footer, model.Description, model.Type, model.Instructions, model.Owner, model.Status, model.AdminContainer, model.AdminSubmission);

            await EditPOST(content.ContentItemId, model.Title, viewModel, async contentItem =>
            {
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);
                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? T["Your content has been published."]
                    : T["Your {0} has been published.", typeDefinition.DisplayName]);
            });

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return Redirect("/AdvancedForms/Admin/Submissions");
        }

        [HttpPost]
        [Route("AdvancedForms/admin/{alias}/Submission/{id}")]
        [FormValueRequired("submit.Publish")]
        public async Task<IActionResult> SubmissionPublish(AdvancedFormViewModel model, string returnUrl = "")
        {
            ContentItem content;
            if (!string.IsNullOrWhiteSpace(model.SubmissionId))
            {
                content = await _contentManager.GetAsync(model.SubmissionId, VersionOptions.Latest);
            }
            else
            {
                content = await _contentManager.NewAsync(_id);
                await _contentManager.CreateAsync(content, VersionOptions.Draft);
            }

            if (string.IsNullOrWhiteSpace(model.Owner))
            {
                model.Owner = User.Identity.Name;
            }

            string guid = content.ContentItemId;
            string subTitle = model.Title + " " + DateTime.Now.ToUniversalTime().ToString() + " " + guid;
            var subObject = JObject.Parse(model.Submission);
            var viewModel = new AdvancedFormSubmissions(model.Submission,
            model.Metadata, subTitle, model.Container, model.Header, model.Footer, model.Description, model.Type, model.Instructions, model.Owner, model.Status, model.AdminContainer, model.AdminSubmission);

            await EditPOST(content.ContentItemId, model.Title, viewModel, async contentItem =>
            {
                await _contentManager.PublishAsync(contentItem);

                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? T["Your content has been published."]
                    : T["Your {0} has been published.", typeDefinition.DisplayName]);
            });

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return Redirect("/AdvancedForms/Admin/Submissions");
        }


        private async Task<IActionResult> EditPOST(string contentItemId, string title, AdvancedFormSubmissions viewModel, Func<ContentItem, Task> conditionallyPublish)
        {

            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.DraftRequired);

            if (contentItem == null)
            {
                return NotFound();
            }

            //if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageAdvancedForms, contentItem))
            //{
            //    return Unauthorized();
            //}

            string guid = contentItem.ContentItemId;
            contentItem.Content.AdvancedFormSubmissions = JToken.FromObject(viewModel);
            contentItem.DisplayText = viewModel.Title;
            contentItem.Author = User.Identity.Name;
            contentItem.Owner = viewModel.Owner;
            contentItem.Content.AutoroutePart.Path = CreatePath(title, guid);

            await conditionallyPublish(contentItem);

            // The content item needs to be marked as saved (again) in case the drivers or the handlers have
            // executed some query which would flush the saved entities. In this case the changes happening in handlers 
            // would not be taken into account.
            _session.Save(contentItem);

            var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);
            return StatusCode(StatusCodes.Status201Created);
        }

        [Route("AdvancedForms/{alias}/Edit/{id}")]
        public async Task<IActionResult> Edit(string alias, string id)
        {
            return await ReturnView(alias, id, EntryType.Edit);
        }

        [Route("AdvancedForms/{alias}/View/{id}")]
        public async Task<IActionResult> View(string alias, string id)
        {
            return await ReturnView(alias, id, EntryType.View);
        }

        [Route("AdvancedForms/{alias}/Print/{id}")]
        public async Task<IActionResult> Print(string alias, string id)
        {
            return await ReturnView(alias, id, EntryType.Print);
        }

        private async Task<IActionResult> ReturnView(string alias, string id, EntryType entryType)
        {
            if (String.IsNullOrWhiteSpace(alias))
            {
                return Redirect("/");
            }
            else if (String.IsNullOrWhiteSpace(id))
            {
                await Display(alias);
            }

            var contentItemId = await _contentAliasManager.GetContentItemIdAsync("slug:AdvancedForms/" + alias);
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Published);
            var subContentItem = await _contentManager.GetAsync(id, VersionOptions.Published);
            var viewName = entryType == EntryType.Print ? "Print" : "Display";

            if (subContentItem == null)
            {
                subContentItem = await _contentManager.GetAsync(id, VersionOptions.Draft);
            }

            if (subContentItem == null)
            {
                return NotFound();
            }

            if (entryType == EntryType.View)
            {
                if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewContent, subContentItem))
                {
                    return Unauthorized();
                }
            }
            else if (!await _authorizationService.AuthorizeAsync(User, Permissions.SubmitForm, subContentItem))
            {
                return Unauthorized();
            }

            var selectedContent = await _contentManager.GetAsync(subContentItem.Content.AdvancedFormSubmissions.Status.Text.ToString(), VersionOptions.Published);

            if (selectedContent == null)
            {
                selectedContent = await _contentManager.GetAsync(subContentItem.Content.AdvancedFormSubmissions.Status.Text.ToString(), VersionOptions.DraftRequired);
            }
            string statusText = string.Empty;
            if (selectedContent != null)
            {
                statusText = selectedContent.DisplayText;
            }


            var model = new AdvancedFormViewModel
            {
                Id = id,
                Owner = subContentItem.Owner,
                Title = contentItem.DisplayText,
                Type = contentItem.Content.AdvancedForm.Type.Text,
                Header = contentItem.Content.AdvancedForm.Header.Html,
                Footer = contentItem.Content.AdvancedForm.Footer.Html,
                Container = contentItem.Content.AdvancedForm.Container.Html,
                AdminContainer = contentItem.Content.AdvancedForm.AdminContainer.Html,
                Description = contentItem.Content.AdvancedForm.Description.Html,
                Instructions = contentItem.Content.AdvancedForm.Instructions.Html,
                SubmissionId = subContentItem.ContentItemId,
                Submission = subContentItem.Content.AdvancedFormSubmissions.Submission.Html,
                AdminSubmission = subContentItem.Content.AdvancedFormSubmissions.AdminSubmission.Html,
                EntryType = entryType,
                Status = subContentItem.Content.AdvancedFormSubmissions.Status.Text,
                StatusText = statusText,
                PublicEditor = new HTMLFieldViewModel() { ID = "PublicComment" }

            };

            return View(viewName, model);

        }

        private string CreatePath(string title, string quid)
        {
            if (!string.IsNullOrEmpty(title))
            {
                title = "AdvancedForms" + "/" + title.Replace(" ", "-") + "/View/" + quid;
            }
            return title;
        }

    }
}
