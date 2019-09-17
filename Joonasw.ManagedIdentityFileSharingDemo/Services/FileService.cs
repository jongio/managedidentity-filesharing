﻿using Joonasw.ManagedIdentityFileSharingDemo.Data;
using Joonasw.ManagedIdentityFileSharingDemo.Extensions;
using Joonasw.ManagedIdentityFileSharingDemo.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Joonasw.ManagedIdentityFileSharingDemo.Services
{
    public class FileService
    {
        private readonly AppDbContext _dbContext;
        private readonly AzureBlobStorageService _blobStorageService;

        public FileService(
            AppDbContext dbContext,
            AzureBlobStorageService blobStorageService)
        {
            _dbContext = dbContext;
            _blobStorageService = blobStorageService;
        }

        public async Task UploadFileAsync(IFormFile file, ClaimsPrincipal user)
        {
            Guid storedBlobId;
            using (var fileStream = file.OpenReadStream())
            {
                storedBlobId = await _blobStorageService.UploadBlobAsync(fileStream, user);
            }

            var storedFile = new StoredFile
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                CreatorObjectId = user.GetObjectId(),
                CreatorTenantId = user.GetTenantId(),
                FileName = file.FileName,
                FileContentType = !string.IsNullOrEmpty(file.ContentType) ? file.ContentType : "application/octet-stream",
                StoredBlobId = storedBlobId
            };
            await _dbContext.StoredFiles.AddAsync(storedFile);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<(Stream stream, string fileName, string contentType)> DownloadFileAsync(Guid id, ClaimsPrincipal user)
        {
            var file = await _dbContext.StoredFiles.SingleAsync(f => f.Id == id);
            FileAccessUtils.CheckAccess(file, user);

            var stream = await _blobStorageService.DownloadBlobAsync(file.StoredBlobId, user);
            return (stream, file.FileName, file.FileContentType);
        }

        public async Task<FileModel[]> GetFilesAsync(ClaimsPrincipal user)
        {
            var files = _dbContext.StoredFiles.ApplyAccessFilter(user);

            return await files
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FileModel
                {
                    Id = f.Id,
                    Name = f.FileName,
                    CreatedAt = f.CreatedAt
                })
                .ToArrayAsync();
        }

        public async Task DeleteFileAsync(Guid id, ClaimsPrincipal user)
        {
            var file = await _dbContext.StoredFiles.SingleAsync(f => f.Id == id);
            FileAccessUtils.CheckAccess(file, user);

            _dbContext.StoredFiles.Remove(file);

            await _blobStorageService.DeleteBlobAsync(file.StoredBlobId, user);

            await _dbContext.SaveChangesAsync();
        }
    }
}
