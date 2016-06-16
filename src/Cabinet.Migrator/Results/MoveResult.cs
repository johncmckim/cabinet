﻿using Cabinet.Core.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cabinet.Migrator.Results {
    public class MoveResult : IMoveResult {
        private readonly string errorMsg;

        private MoveResult(string sourceKey, string destKey) {
            if (String.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentNullException(nameof(sourceKey));
            if (String.IsNullOrWhiteSpace(destKey)) throw new ArgumentNullException(nameof(destKey));
            this.SourceKey = sourceKey;
            this.DestKey = destKey;
        }

        public MoveResult(string sourceKey, string destKey, bool success = true, string errorMsg = null)
            : this(sourceKey, destKey) {
            this.Success = success;
            this.errorMsg = errorMsg;
        }

        public MoveResult(string sourceKey, string destKey, Exception e, string errorMsg = null)
            : this(sourceKey, destKey) {
            if (e == null) throw new ArgumentNullException(nameof(e));
            this.SourceKey = sourceKey;
            this.DestKey = destKey;
            this.Exception = e;
            this.errorMsg = errorMsg ?? e?.ToString();
            this.Success = false;
        }

        public string SourceKey { get; private set; }
        public string DestKey { get; private set; }
        public bool Success { get; private set; }

        public Exception Exception { get; private set; }

        public bool AlreadyExists { get; set; }

        public string GetErrorMessage() {
            return errorMsg;
        }
    }
}
