﻿/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESSOURCES
 */
using Gravity.Services.DataContracts;

using LiteDB;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Rhino.Api.Contracts;
using Rhino.Controllers.Domain.Interfaces;
using Rhino.Controllers.Extensions;
using Rhino.Controllers.Models;
using Rhino.Controllers.Models.Server;
using Rhino.Settings;

using Swashbuckle.AspNetCore.Annotations;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;

using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Rhino.Controllers.Controllers
{
    [ApiVersion($"{AppSettings.ApiVersion}.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class PluginsController : ControllerBase
    {
        // members: state
        private readonly IDomain _domain;

        // members: private properties
        private Authentication Authentication => Request.GetAuthentication();

        /// <summary>
        /// Creates a new instance of <see cref="ControllerBase"/>.
        /// </summary>
        /// <param name="domain">An IDomain implementation to use with the Controller.</param>
        public PluginsController(IDomain domain)
        {
            _domain = domain;
        }

        #region *** Get    ***
        // GET: api/v3/plugins
        [HttpGet]
        [HttpGet]
        [SwaggerOperation(
            Summary = "Get-Plugin -All",
            Description = "Returns a list of available _**Rhino Plugins**_ content.")]
        [Produces(MediaTypeNames.Text.Plain)]
        [SwaggerResponse(StatusCodes.Status200OK, SwaggerDocument.StatusCode.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(string))]
        public IActionResult Get()
        {
            // get response
            var entities = InvokeGet(id: string.Empty).Entities;
            Response.Headers[RhinoResponseHeader.CountTotalSpecs] = $"{entities.Count()}";

            // get
            return Ok(string.Join(Utilities.Separator, entities));
        }

        // GET: api/v3/plugins/:id
        [HttpGet("{id}")]
        [SwaggerOperation(
            Summary = "Get-Plugin -Id {00000000-0000-0000-0000-000000000000}",
            Description = "Returns an existing _**Rhino Plugins**_ content.")]
        [Produces(MediaTypeNames.Text.Plain, MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status200OK, SwaggerDocument.StatusCode.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, SwaggerDocument.StatusCode.Status404NotFound, Type = typeof(GenericErrorModel<string>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(GenericErrorModel<string>))]
        public async Task<IActionResult> Get([SwaggerParameter(SwaggerDocument.Parameter.Id)] string id)
        {
            // get data
            var (statusCode, entity) = InvokeGet(id);

            // exit conditions
            if (statusCode == StatusCodes.Status404NotFound)
            {
                return await this
                    .ErrorResultAsync<string>($"Get-plugin -Id {id} = NotFound", StatusCodes.Status404NotFound)
                    .ConfigureAwait(false);
            }

            // return
            Response.Headers[RhinoResponseHeader.CountTotalSpecs] = $"{entity.Count()}";
            return Ok(entity.FirstOrDefault());
        }

        private (int StatusCode, IEnumerable<string> Entities) InvokeGet(string id)
        {
            // get all
            if (string.IsNullOrEmpty(id))
            {
                var plugins = _domain.Plugins.SetAuthentication(Authentication).Get();
                return (StatusCodes.Status200OK, plugins);
            }

            // get one
            var (statusCode, entity) = _domain.Plugins.SetAuthentication(Authentication).Get(id);

            // setup
            return (statusCode, new[] { entity });
        }

        // GET: api/v3/plugins/export
        [HttpGet("export")]
        [SwaggerOperation(
            Summary = "Export-Plugins",
            Description = "Downloads the entire `Plugins` folder as `ZIP Archive`.")]
        [Produces(MediaTypeNames.Application.Octet)]
        [SwaggerResponse(StatusCodes.Status200OK, SwaggerDocument.StatusCode.Status200OK, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status404NotFound, SwaggerDocument.StatusCode.Status404NotFound, Type = typeof(GenericErrorModel<string>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(GenericErrorModel<string>))]
        public async Task<IActionResult> Export()
        {
            // cleanup
            var filePath = Path.Combine(Environment.CurrentDirectory, "Plugins.zip");
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            // get data
            var (statusCode, stream) = _domain.Plugins.ExportPlugins();

            // not found
            if (statusCode == StatusCodes.Status404NotFound)
            {
                return await this
                    .ErrorResultAsync<string>("Export-Plugins = (NotFound | NoPlugins)", StatusCodes.Status404NotFound)
                    .ConfigureAwait(false);
            }

            // server error
            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                return await this
                    .ErrorResultAsync<string>("Export-Plugins = InternalServerError", StatusCodes.Status500InternalServerError)
                    .ConfigureAwait(false);
            }

            // get
            return File(stream, MediaTypeNames.Application.Octet, "Plugins.zip");
        }
        #endregion

        #region *** Post   ***
        // POST: api/v3/plugins
        [HttpPost]
        [SwaggerOperation(
            Summary = "Create-Plugin",
            Description = "Creates new or updates existing one or more _**Rhino Plugin**_.")]
        [Consumes(MediaTypeNames.Text.Plain)]
        [Produces(MediaTypeNames.Text.Plain, MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status201Created, SwaggerDocument.StatusCode.Status201Created, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, SwaggerDocument.StatusCode.Status400BadRequest, Type = typeof(GenericErrorModel<string>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(GenericErrorModel<string>))]
        public async Task<IActionResult> Post([FromQuery(Name = "prvt"), SwaggerParameter(SwaggerDocument.Parameter.Private)] bool isPrivate)
        {
            // setup
            var pluginSpecs = (await Request.ReadAsync().ConfigureAwait(false))
                .Split(RhinoSpecification.Separator)
                .Select(i => i.Trim().NormalizeLineBreaks());

            // create plugins
            _domain.Plugins.SetAuthentication(Authentication);
            var plugins = _domain.Plugins.Add(pluginSpecs, isPrivate);

            // response
            if (string.IsNullOrEmpty(plugins))
            {
                return Ok();
            }

            // setup            
            var okResponse = _domain.Plugins.SetAuthentication(Authentication).Get();
            Response.Headers[RhinoResponseHeader.CountTotalSpecs] = $"{okResponse.Count()}";

            // get
            return Created("/api/v3/plugins", string.Join(Utilities.Separator, okResponse));
        }

        // POST: api/v3/plugins/submit
        [HttpPost("submit")]
        [SwaggerOperation(
            Summary = "Submit-Plugin",
            Description = "Submits a plugins code package or updates an existing one or more _**Rhino Plugins Package**_.")]
        [Consumes(MediaTypeNames.Application.Json)]
        [Produces(MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status201Created, SwaggerDocument.StatusCode.Status201Created, Type = typeof(object))]
        [SwaggerResponse(StatusCodes.Status400BadRequest, SwaggerDocument.StatusCode.Status400BadRequest, Type = typeof(GenericErrorModel<string>))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(GenericErrorModel<string>))]
        public async Task<IActionResult> Submit([FromBody] PackageUploadModel model)
        {
            // setup
            _domain.Plugins.SetAuthentication(Authentication);

            // submit
            var response = await _domain.Plugins.SubmitAsync(model);

            // bad request
            if (response.StatusCode == StatusCodes.Status400BadRequest)
            {
                var badRequest = $"Submit-plugin -Id {model?.Id} = (BadRequest | {response.Message})";
                return await this
                    .ErrorResultAsync<string>(badRequest, StatusCodes.Status400BadRequest)
                    .ConfigureAwait(false);
            }

            // bad request
            if (response.StatusCode == StatusCodes.Status500InternalServerError)
            {
                var serverError = $"Submit-plugin -Id {model?.Id} = (InternalServerError | {response.Message})";
                return await this
                    .ErrorResultAsync<string>(serverError, StatusCodes.Status500InternalServerError)
                    .ConfigureAwait(false);
            }

            // sync assemblies
            _domain.Plugins.SyncAssemblies();

            // get
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return new ContentResult
            {
                Content = JsonSerializer.Serialize(new { response.Message }, options),
                ContentType = MediaTypeNames.Application.Json,
                StatusCode = response.StatusCode
            };
        }

        // POST: api/v3/plugins/sync
        [Obsolete(message: "This endpoint is obsolete and will be removed in future versions. Please use `/api/v3/cache/sync` instead.")]
        [HttpPost("sync")]
        [SwaggerOperation(
            Summary = "Sync-Assemblies",
            Description = "Syncs the assemblies collection under the domain.")]
        [Produces(MediaTypeNames.Application.Json)]
        [SwaggerResponse(StatusCodes.Status204NoContent, SwaggerDocument.StatusCode.Status201Created, Type = typeof(object))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(GenericErrorModel<string>))]
        public async Task<IActionResult> SyncAssemblies()
        {
            // setup
            _domain.Plugins.SetAuthentication(Authentication);

            // submit
            var response = _domain.Plugins.SyncAssemblies();

            // bad request
            if (response.StatusCode == StatusCodes.Status500InternalServerError)
            {
                var serverError = $"Syncs-Assemblies = (InternalServerError | {response.Message})";
                return await this
                    .ErrorResultAsync<string>(serverError, StatusCodes.Status500InternalServerError)
                    .ConfigureAwait(false);
            }

            // get
            return new ContentResult
            {
                ContentType = MediaTypeNames.Application.Json,
                StatusCode = response.StatusCode
            };
        }
        #endregion

        #region *** Delete ***
        // DELETE: api/v3/plugins/:id
        [HttpDelete("{id}")]
        [SwaggerOperation(
            Summary = "Delete-Plugin -Id {00000000-0000-0000-0000-000000000000}",
            Description = "Deletes an existing _**Rhino Plugin**_.")]
        [SwaggerResponse(StatusCodes.Status204NoContent, SwaggerDocument.StatusCode.Status204NoContent)]
        [SwaggerResponse(StatusCodes.Status404NotFound, SwaggerDocument.StatusCode.Status404NotFound, Type = typeof(string))]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(string))]
        public async Task<IActionResult> Delete([SwaggerParameter(SwaggerDocument.Parameter.Id)] string id)
        {
            // delete
            var statusCode = _domain.Plugins.SetAuthentication(Authentication).Delete(id);

            // results
            return statusCode == StatusCodes.Status404NotFound
                ? await this.ErrorResultAsync<string>($"Delete-Plugin -id {id} = NotFound", statusCode).ConfigureAwait(false)
                : NoContent();
        }

        // DELETE: api/v3/plugins
        [HttpDelete]
        [SwaggerOperation(
            Summary = "Delete-Plugin -All",
            Description = "Deletes all existing _**Rhino Plugin**_.")]
        [SwaggerResponse(StatusCodes.Status204NoContent, SwaggerDocument.StatusCode.Status204NoContent)]
        [SwaggerResponse(StatusCodes.Status500InternalServerError, SwaggerDocument.StatusCode.Status500InternalServerError, Type = typeof(string))]
        public IActionResult Delete()
        {
            // get credentials
            _domain.Plugins.SetAuthentication(Authentication).Delete();

            // results
            return NoContent();
        }
        #endregion
    }
}
