﻿using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Cabinet.Core;
using Cabinet.Core.Providers;
using Cabinet.S3;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cabinet.Tests.S3 {
    public class AmazonS3StorageProviderFacts {
        private const string ValidBucketName = "bucket-name";
        private const string ValidFileKey = "key";

        private readonly Mock<IAmazonS3ClientFactory> mockS3ClientFactory;
        private readonly Mock<IAmazonS3> mockS3Client;
        private readonly Mock<ITransferUtility> mockS3TransferUtility;

        public AmazonS3StorageProviderFacts() {
            this.mockS3ClientFactory = new Mock<IAmazonS3ClientFactory>();
            this.mockS3Client = new Mock<IAmazonS3>();
            this.mockS3TransferUtility = new Mock<ITransferUtility>();

            this.mockS3ClientFactory.Setup(f => f.GetS3Client(It.IsAny<AmazonS3CabinetConfig>())).Returns(this.mockS3Client.Object);
            this.mockS3ClientFactory.Setup(f => f.GetTransferUtility(mockS3Client.Object)).Returns(this.mockS3TransferUtility.Object);
        }

        [Fact]
        public void Provider_Type() {
            IStorageProvider<AmazonS3CabinetConfig> provider = GetProvider();
            Assert.Equal(AmazonS3CabinetConfig.ProviderType, provider.ProviderType);
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Exists_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.ExistsAsync(key, config));
        }

        [Fact]
        public async Task Exists_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.ExistsAsync(ValidFileKey, config));
        }

        [Theory]
        [InlineData("test-bucket", null, "test-key", HttpStatusCode.OK, true, "test-key")]
        [InlineData("test-bucket", "folder", "test-key", HttpStatusCode.OK, true, "folder/test-key")]
        [InlineData("test-bucket", "", "test-key", HttpStatusCode.NotFound, false, "test-key")]
        [InlineData("test-bucket", "", "test-key", HttpStatusCode.Forbidden, false, "test-key")]
        [InlineData("test-bucket", "folder", "test-key", HttpStatusCode.Unauthorized, false, "folder/test-key")]
        public async Task Exists(string bucketName, string keyPrefix, string key, HttpStatusCode code, bool expectedExists, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(bucketName, keyPrefix);

            SetupGetObjectRequest(bucketName, expectedKey, code);

            bool actualExists = await provider.ExistsAsync(key, config);
            Assert.Equal(expectedExists, expectedExists);
        }

        [Fact]
        public async Task List_Keys_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.ListKeysAsync(config));
        }

        [Theory]
        [MemberData("GetTestS3Objects")]
        public async Task List_Keys(string bucketName, string configKeyPrefix, string keyPrefix, bool recursive, HttpStatusCode code, List<S3Object> expectedS3Objects, string expectedKeyPrefix) {
            var provider = GetProvider();
            var config = GetConfig(bucketName, configKeyPrefix);

            SetupGetObjectsRequest(bucketName, expectedKeyPrefix, recursive, config.Delimiter, code, expectedS3Objects);

            var keys = await provider.ListKeysAsync(config, keyPrefix: keyPrefix, recursive: recursive);
            var keysList = keys.ToList();
            var expectedKeys = expectedS3Objects.Select(o => o.Key).ToList();
            Assert.Equal(expectedKeys, keysList);
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Get_File_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetFileAsync(key, config));
        }

        [Fact]
        public async Task Get_File_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetFileAsync(ValidFileKey, config));
        }

        [Theory]
        [InlineData("test-bucket", null, "test-key", HttpStatusCode.OK, true, "test-key")]
        [InlineData("test-bucket", "folder", "test-key", HttpStatusCode.OK, true, "folder/test-key")]
        [InlineData("test-bucket", "", "test-key", HttpStatusCode.NotFound, false, "test-key")]
        [InlineData("test-bucket", "", "test-key", HttpStatusCode.Forbidden, false, "test-key")]
        [InlineData("test-bucket", "folder", "test-key", HttpStatusCode.Unauthorized, false, "folder/test-key")]
        public async Task Get_File(string bucketName, string keyPrefix, string key, HttpStatusCode code, bool expectedExists, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(bucketName, keyPrefix);

            SetupGetObjectRequest(bucketName, expectedKey, code);

            var file = await provider.GetFileAsync(key, config);

            Assert.Equal(key, file.Key);
            Assert.Equal(expectedExists, file.Exists);
        }

        [Fact]
        public async Task Get_Items_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetItemsAsync(config));
        }

        [Theory]
        [MemberData("GetTestS3Objects")]
        public async Task Get_Items(string bucketName, string configKeyPrefix, string keyPrefix, bool recursive, HttpStatusCode code, List<S3Object> expectedS3Objects, string expectedKeyPrefix) {
            var provider = GetProvider();
            var config = GetConfig(bucketName, configKeyPrefix);

            SetupGetObjectsRequest(bucketName, expectedKeyPrefix, recursive, config.Delimiter, code, expectedS3Objects);

            var expectedFileInfos = expectedS3Objects.Select(o => new AmazonS3CabinetItemInfo(o.Key, true, ItemType.File) {
                LastModifiedUtc = o.LastModified
            });

            var fileInfos = await provider.GetItemsAsync(config, keyPrefix: keyPrefix, recursive: recursive);
            
            Assert.Equal(expectedFileInfos, fileInfos, new CabinetItemInfoKeyComparer());
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Open_Read_Stream_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.OpenReadStreamAsync(key, config));
        }

        [Fact]
        public async Task Open_Read_Stream_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.OpenReadStreamAsync(ValidFileKey, config));
        }

        [Theory]
        [InlineData("", ValidFileKey, ValidFileKey)]
        [InlineData("folder", ValidFileKey, "folder/" + ValidFileKey)]
        public async Task Open_Read_Stream(string keyPrefix, string key, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);
            var mockStream = new Mock<Stream>();

            this.mockS3TransferUtility
                    .Setup(t => t.OpenStreamAsync(config.BucketName, expectedKey, default(CancellationToken)))
                    .ReturnsAsync(mockStream.Object);

            var stream = await provider.OpenReadStreamAsync(key, config);

            Assert.Equal(mockStream.Object, stream);
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Save_File_Path_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.SaveFileAsync(key, filePath, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }
        
        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Save_File_Path_Empty_FilePath_Throws(string filePath) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.SaveFileAsync(ValidFileKey, filePath, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }

        [Fact]
        public async Task Save_File_Path_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.SaveFileAsync(ValidFileKey, filePath, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }

        [Theory]
        [InlineData("", ValidFileKey, ValidFileKey)]
        [InlineData("folder", ValidFileKey, "folder/" + ValidFileKey)]
        public async Task Save_File_Path(string keyPrefix, string key, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            this.mockS3TransferUtility
                    .Setup(t =>
                        t.UploadAsync(
                            It.Is<TransferUtilityUploadRequest>(r => r.Key == expectedKey),
                            default(CancellationToken)
                        )
                    )
                    .Returns(Task.FromResult(0));

            var result = await provider.SaveFileAsync(key, filePath, HandleExistingMethod.Overwrite, mockProgress.Object, config);

            Assert.True(result.Success);
            Assert.Equal(key, result.Key);
        }

        [Theory]
        [InlineData("", ValidFileKey, ValidFileKey)]
        [InlineData("folder", ValidFileKey, "folder/" + ValidFileKey)]
        public async Task Save_File_Path_Exists_Overwrite(string keyPrefix, string key, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            SetupGetObjectRequest(ValidBucketName, expectedKey, HttpStatusCode.OK);

            this.mockS3TransferUtility
                    .Setup(t => 
                        t.UploadAsync(
                            It.Is<TransferUtilityUploadRequest>(r => r.Key == expectedKey),
                            default(CancellationToken)
                        )
                    )
                    .Returns(Task.FromResult(0));

            var result = await provider.SaveFileAsync(key, filePath, HandleExistingMethod.Overwrite, mockProgress.Object, config);

            Assert.True(result.Success);
            Assert.Equal(key, result.Key);
        }

        [Fact]
        public async Task Save_File_Path_Exists_Skip() {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);

            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<NotImplementedException>(async () => {
                await provider.SaveFileAsync(ValidFileKey, filePath, HandleExistingMethod.Skip, mockProgress.Object, config);
            });
        }

        [Fact]
        public async Task Save_File_Path_Exists_Throw() {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);

            string filePath = @"C:\test\test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<NotImplementedException>(async () => {
                await provider.SaveFileAsync(ValidFileKey, filePath, HandleExistingMethod.Throw, mockProgress.Object, config);
            });
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Save_File_Stream_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () => 
                await provider.SaveFileAsync(key, mockStream.Object, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }

        [Fact]
        public async Task Save_File_Stream_Null_Content_Throws() {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            Stream stream = null;
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.SaveFileAsync(ValidFileKey, stream, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }

        [Fact]
        public async Task Save_File_Stream_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.SaveFileAsync(ValidFileKey, mockStream.Object, HandleExistingMethod.Overwrite, mockProgress.Object, config)
            );
        }

        [Theory]
        [InlineData("", ValidFileKey, ValidFileKey)]
        [InlineData("folder", ValidFileKey, "folder/" + ValidFileKey)]
        public async Task Save_File_Stream(string keyPrefix, string key, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            this.mockS3TransferUtility
                    .Setup(t => 
                        t.UploadAsync(
                            It.Is<TransferUtilityUploadRequest>(r => r.Key == expectedKey),
                            default(CancellationToken)
                        )
                    )
                    .Returns(Task.FromResult(0));

            var result = await provider.SaveFileAsync(key, mockStream.Object, HandleExistingMethod.Overwrite, mockProgress.Object, config);

            Assert.True(result.Success);
            Assert.Equal(key, result.Key);
        }

        [Theory]
        [InlineData("", ValidFileKey, ValidFileKey)]
        [InlineData("folder", ValidFileKey, "folder/" + ValidFileKey)]
        public async Task Save_File_Stream_Exists_Overwrite(string keyPrefix, string key, string expectedKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            SetupGetObjectRequest(ValidBucketName, expectedKey, HttpStatusCode.OK);

            this.mockS3TransferUtility
                    .Setup(t => t.UploadAsync(It.IsAny<TransferUtilityUploadRequest>(), default(CancellationToken)))
                    .Returns(Task.FromResult(0));

            var result = await provider.SaveFileAsync(key, mockStream.Object, HandleExistingMethod.Overwrite, mockProgress.Object, config);

            Assert.True(result.Success);
            Assert.Equal(key, result.Key);
        }

        [Fact]
        public async Task Save_File_Stream_Exists_Skip() {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);

            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();
            
            await Assert.ThrowsAsync<NotImplementedException>(async () => {
                await provider.SaveFileAsync(ValidFileKey, mockStream.Object, HandleExistingMethod.Skip, mockProgress.Object, config);
            });
        }

        [Fact]
        public async Task Save_File_Stream_Exists_Throw() {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);

            var mockStream = new Mock<Stream>();
            var mockProgress = new Mock<IProgress<IWriteProgress>>();
            
            await Assert.ThrowsAsync<NotImplementedException>(async () => {
                await provider.SaveFileAsync(ValidFileKey, mockStream.Object, HandleExistingMethod.Throw, mockProgress.Object, config);
            });
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Move_File_Path_Empty_SourceKey_Throws(string sourceKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            string destKey = @"test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.MoveFileAsync(sourceKey, destKey, HandleExistingMethod.Overwrite, config)
            );
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Move_File_Path_Empty_DestKey_Throws(string destKey) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            string sourceKey = @"test.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.MoveFileAsync(sourceKey, destKey, HandleExistingMethod.Overwrite, config)
            );
        }

        [Fact]
        public async Task Move_File_Path_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            string sourceKey = @"source.txt";
            string destKey = @"dest.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.MoveFileAsync(sourceKey, destKey, HandleExistingMethod.Overwrite, config)
            );
        }

        [Theory]
        [InlineData(HandleExistingMethod.Skip), InlineData(HandleExistingMethod.Throw)]
        public async Task Move_File_Not_Overwrite_Throws(HandleExistingMethod handleExisting) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName);
            string sourceKey = @"source.txt";
            string destKey = @"dest.txt";
            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            await Assert.ThrowsAsync<NotImplementedException>(async () =>
                await provider.MoveFileAsync(sourceKey, destKey, handleExisting, config)
            );
        }

        [Theory]
        [InlineData(null, "source.txt", "dest.txt", "source.txt", "dest.txt", HttpStatusCode.OK)]
        [InlineData("folder", "source.txt", "dest.txt", "folder/source.txt", "folder/dest.txt", HttpStatusCode.OK)]
        [InlineData("", "source.txt", "dest.txt", "source.txt", "dest.txt", HttpStatusCode.NotFound)]
        public async Task Move_File(string keyPrefix, string sourceKey, string destKey, string expectedSourceKey, string expectedDestKey, HttpStatusCode copyResult) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            var mockProgress = new Mock<IProgress<IWriteProgress>>();

            this.mockS3Client.Setup(s3 => 
                s3.CopyObjectAsync(
                    It.Is<CopyObjectRequest>((r) => r.SourceKey == expectedSourceKey && r.DestinationKey == expectedDestKey),
                    default(CancellationToken)
                ))
                .ReturnsAsync(new CopyObjectResponse {
                    HttpStatusCode = copyResult
                });

            this.mockS3Client.Setup(s3 => 
                s3.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(r => r.Key == sourceKey),
                    default(CancellationToken)
                ))
                .ReturnsAsync(new DeleteObjectResponse {
                    HttpStatusCode = HttpStatusCode.OK
                });

            var result = await provider.MoveFileAsync(sourceKey, destKey, HandleExistingMethod.Overwrite, config);

            bool shouldDelete = copyResult == HttpStatusCode.OK;
            var deleteTimes = shouldDelete ? Times.Once() : Times.Never();

            this.mockS3Client.Verify(s3 => s3.CopyObjectAsync(It.IsAny<CopyObjectRequest>(), default(CancellationToken)), Times.Once);
            this.mockS3Client.Verify(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default(CancellationToken)), deleteTimes);
        }

        [Theory]
        [InlineData(null), InlineData(""), InlineData(" ")]
        public async Task Delete_File_Empty_Key_Throws(string key) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, null);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.DeleteFileAsync(key, config));
        }

        [Fact]
        public async Task Delete_File_Null_Config_Throws() {
            var provider = GetProvider();
            AmazonS3CabinetConfig config = null;
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.DeleteFileAsync(ValidFileKey, config));
        }

        [Theory]
        [InlineData(null, "file.txt", "file.txt", HttpStatusCode.OK)]
        [InlineData("folder", "file.txt", "folder/file.txt", HttpStatusCode.OK)]
        [InlineData("", "file.txt", "file.txt", HttpStatusCode.NotFound)]
        public async Task Delete_File(string keyPrefix, string key, string expectedKey, HttpStatusCode httpResult) {
            var provider = GetProvider();
            var config = GetConfig(ValidBucketName, keyPrefix);

            var mockProgress = new Mock<IProgress<IWriteProgress>>();
            
            this.mockS3Client.Setup(s3 => 
                s3.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>((r) => r.Key == expectedKey),
                    default(CancellationToken)
                ))
                .ReturnsAsync(new DeleteObjectResponse {
                    HttpStatusCode = httpResult
                });

            var result = await provider.DeleteFileAsync(key, config);

            this.mockS3Client.Verify(s3 => s3.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default(CancellationToken)), Times.Once);
        }
        private void SetupGetObjectRequest(string bucketName, string key, HttpStatusCode code) {
            this.mockS3Client.Setup(
                s3 => s3.GetObjectAsync(It.Is<GetObjectRequest>((r) => r.BucketName == bucketName && r.Key == key), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new GetObjectResponse() {
                HttpStatusCode = code,
                Key = key
            });
        }

        private void SetupGetObjectsRequest(string bucketName, string expectedKeyPrefix, bool recursive, string delimiter, HttpStatusCode code, List<S3Object> s3Objects) {
            this.mockS3Client.Setup(
                s3 => s3.ListObjectsAsync(
                    It.Is<ListObjectsRequest>((r) => 
                        r.BucketName == bucketName && 
                        r.Prefix == expectedKeyPrefix &&
                        (recursive ? String.IsNullOrWhiteSpace(r.Delimiter) : r.Delimiter == delimiter)
                    ), 
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new ListObjectsResponse() {
                HttpStatusCode = code,
                S3Objects = s3Objects
            });
        }

        private AmazonS3StorageProvider GetProvider() {
            var provider = new AmazonS3StorageProvider(this.mockS3ClientFactory.Object);
            return provider;
        }

        private AmazonS3CabinetConfig GetConfig(string bucketName, string keyPrefix = null) {
            var config = new AmazonS3CabinetConfig(bucketName, RegionEndpoint.APSoutheast2, null) {
                KeyPrefix = keyPrefix
            };
            return config;
        }

        public static object[] GetTestS3Objects() {
            var s3Objects = new List<S3Object>() {
                new S3Object() { Key = "file.txt", Size = 3, LastModified = DateTime.UtcNow.AddHours(-1) },
                new S3Object() { Key = @"bar/one.txt", Size = 3, LastModified = DateTime.UtcNow.AddHours(-5) },
                new S3Object() { Key = @"bar/two.txt", Size = 3, LastModified = DateTime.UtcNow.AddMinutes(-1) },
                new S3Object() { Key = @"bar/baz/three", Size = 3, LastModified = DateTime.UtcNow.AddHours(-8) },
                new S3Object() { Key = @"foo/one.txt", Size = 3, LastModified = DateTime.UtcNow.AddSeconds(-10) },
            };

            // ListObjectsRequest has no concept of recurisve or not, it just is recursive
            // The response under the prefix will be all the keys with that prefix

            string barPrefix = "bar";
            string bazPrefix = "baz";
            string barBazPrefix = "bar/baz";

            var barObjects = s3Objects.Where(o => o.Key.StartsWith(barPrefix)).ToList();
            var bazObjects = s3Objects.Where(o => o.Key.StartsWith(barBazPrefix)).ToList();
            var barDirectChildObjects = barObjects.Where(o => o.Key.StartsWith(barPrefix) && !o.Key.StartsWith(barBazPrefix + "/")).ToList();

            return new object[] {
                new object[] { "test-bucket", "",        "",        true, HttpStatusCode.OK, s3Objects,  "" },
                new object[] { "test-bucket", "",        barPrefix, true, HttpStatusCode.OK, barObjects, barPrefix },
                new object[] { "test-bucket", barPrefix, "",        true, HttpStatusCode.OK, barObjects, barPrefix },
                new object[] { "test-bucket", barPrefix, bazPrefix, true, HttpStatusCode.OK, bazObjects, barBazPrefix },

                new object[] { "test-bucket", "", "",        false, HttpStatusCode.OK, s3Objects.Where(o => o.Key == "file.txt").ToList(), "" },
                new object[] { "test-bucket", "", barPrefix, false, HttpStatusCode.OK, barDirectChildObjects,                               barPrefix },
            };
        }
    }
}
