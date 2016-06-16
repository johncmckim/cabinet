﻿using Cabinet.Core;
using Cabinet.Core.Providers;
using Cabinet.FileSystem;
using Cabinet.Web.Files;
using Cabinet.Web.SelfHostTest.Framework;
using Cabinet.Web.Validation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace Cabinet.Web.SelfHostTest.Controllers {
    [RoutePrefix("upload")]
    public class UploadController : ApiController {
        private const string TempFolder = "~/App_Data/Temp";

        private readonly IFileCabinet fileCabinet;
        private readonly IUploadValidator uploadValidator;
        private readonly IKeyProvider keyProvider;
        private readonly IPathMapper pathMapper;

        public UploadController(IFileCabinet fileCabinet, IUploadValidator uploadValidator, IKeyProvider keyProvider, IPathMapper pathMapper) {
            this.fileCabinet = fileCabinet;
            this.uploadValidator = uploadValidator;
            this.keyProvider = keyProvider;
            this.pathMapper = pathMapper;
        }

        [Route(""), HttpPost]
        public async Task<IHttpActionResult> Post() {
            if (!Request.Content.IsMimeMultipartContent()) {
                return this.StatusCode(HttpStatusCode.UnsupportedMediaType);
            }

            string tempPath = this.pathMapper.MapPath(TempFolder);

            if(!Directory.Exists(tempPath)) {
                Directory.CreateDirectory(tempPath);
            }

            var localFileProgress = new Progress<IWriteProgress>();
            var cabinetFileProgress = new Progress<IWriteProgress>();

            localFileProgress.ProgressChanged += (object sender, IWriteProgress e) => {
                // Notify Message Bus to return progress to client (i.e. SignalR)
                Console.WriteLine("Uploaded {0} bytes to temp", e.BytesWritten);
            };

            cabinetFileProgress.ProgressChanged += (object sender, IWriteProgress e) => {
                // Notify Message Bus to return progress to client (i.e. SignalR)
                Console.WriteLine("Uploaded {0} bytes to cabinet", e.BytesWritten);
            };

            var provider = new FileCabinetStreamProvider(fileCabinet, uploadValidator, keyProvider, tempPath) {
                LocalFileUploadProgress = localFileProgress,
                CabinetFileSaveProgress = cabinetFileProgress
            };

            // Read to disk temporarily
            await Request.Content.ReadAsMultipartAsync(provider);

            // Save in cabinet
            var result = await provider.SaveInCabinet();

            foreach(var r in result) {
                if (r.Success) {
                    Console.WriteLine("Saved file to {0}", r.Key);
                } else {
                    Console.WriteLine("Failed to save file to {0}.", r.Key);
                    Console.WriteLine("Save error: {0}", r.GetErrorMessage());
                }
            }

            if(result.Any(r => !r.Success)) {
                return this.StatusCode(HttpStatusCode.InternalServerError);
            }

            return this.StatusCode(HttpStatusCode.Created);
        }
    }
}
