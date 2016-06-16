﻿using Cabinet.Config;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Cabinet.Tests.Config {
    public class FileCabinetProviderConfigStoreFacts {
        private const string ConfigPath = @"c:\test\config";

        private readonly Mock<IFileCabinetConfigConvertFactory> mockConverterFactory;
        private readonly Mock<ICabinetProviderConfigConverter> mockConverter;
        private readonly MockFileSystem mockFs;

        public FileCabinetProviderConfigStoreFacts() {
            this.mockConverterFactory = new Mock<IFileCabinetConfigConvertFactory>();
            this.mockConverter = new Mock<ICabinetProviderConfigConverter>();
            this.mockFs = new MockFileSystem();
            this.mockConverterFactory.Setup(f => f.GetConverter(It.IsAny<string>())).Returns(mockConverter.Object);
        }

        [Theory]
        [InlineData("ondisk", "FileSystem"), InlineData("amazon", "AmazonS3")]
        public void Get_Config(string name, string type) {
            SetupValidConfig();

            var store = GetConfigStore();

            var config = store.GetConfig(name);
            
            this.mockConverterFactory.Verify(f => f.GetConverter(type), Times.Once);
            this.mockConverter.Verify(c => c.ToConfig(It.IsAny<JToken>()), Times.Once);
        }

        private void SetupValidConfig() {
            string configJsonString = @"{
    ""ondisk"": {
        ""type"": ""FileSystem"",
        ""config"": {}
    },
    ""amazon"": {
        ""type"": ""AmazonS3"",
        ""config"": {}
    }
}";
            this.mockFs.AddFile(ConfigPath, new MockFileData(configJsonString));
        }

        private FileCabinetProviderConfigStore GetConfigStore() {
            var store = new FileCabinetProviderConfigStore(ConfigPath, mockConverterFactory.Object, mockFs);
            return store;
        }
    }
}