﻿// Copyright (C) Arctium Software.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Lappa.ORM.Constants;
using Lappa.ORM.Logging;
using Lappa.ORM.Managers;

namespace Lappa.ORM
{
    public partial class Database
    {
        public DatabaseType Type { get; private set; }
        public ILog Log { get; private set; }

        string connectionString;
        Connector connector;
        ConnectorQuery connectorQuery;
        EntityBuilder entityBuilder;

        public Database()
        {
            // Initialize the cache manager on first database creation. 
            CacheManager.Instance.Initialize();

            // Initialize dummy logger.
            Log = new Log();

            connector = new Connector();
        }

        public bool Initialize(string connString, DatabaseType type, bool loadConnectorFromFile = true)
        {
            return InitializeAsync(connString, type, loadConnectorFromFile).GetAwaiter().GetResult();
        }

        public async Task<bool> InitializeAsync(string connString, DatabaseType type, bool loadConnectorFromFile = true)
        {
            Type = type;

            connectionString = connString;
            connectorQuery = new ConnectorQuery(type);

            entityBuilder = new EntityBuilder(this);

            try
            {
                connector.Load(type, loadConnectorFromFile);

                using (var connection = await CreateConnectionAsync())
                    return connection.State == ConnectionState.Open;
            }
            catch (Exception ex)
            {
                Log.Message(LogTypes.Error, ex.ToString());

                return false;
            }
        }

        // Overwrite dummy logger.
        // Can be called at any time.
        public void SetLogger(ILog logger) => Log = logger;

        // Default for MySql is the current assembly directory.
        // Must be called before Initialize.
        public void SetConnectorFilePath(string connectorFilePath) => connector.FilePath = connectorFilePath;

        // Default for MySql is "MySql.Data.dll".
        // Must be called before Initialize.
        public void SetConnectorFileName(string connectorFileName) => connector.FileName = connectorFileName;

        DbConnection CreateConnection() => CreateConnectionAsync().GetAwaiter().GetResult();

        internal async Task<DbConnection> CreateConnectionAsync()
        {
            var connection = connector.CreateConnectionObject();

            connection.ConnectionString = connectionString;

            await connection.OpenAsync();

            return connection;
        }

        internal DbCommand CreateSqlCommand(DbConnection connection, string sql, params object[] args)
        {
            var sqlCommand = connector.CreateCommandObject();

            sqlCommand.Connection = connection;
            sqlCommand.CommandText = sql;
            sqlCommand.CommandTimeout = 2147483;

            if (args.Length > 0)
            {
                var mParams = new DbParameter[args.Length];

                for (var i = 0; i < args.Length; i++)
                {
                    var param = connector.CreateParameterObject();

                    param.ParameterName = "";
                    param.Value = args[i];

                    mParams[i] = param;
                }

                sqlCommand.Parameters.AddRange(mParams);
            }

            return sqlCommand;
        }

        internal bool Execute(string sql, params object[] args) => ExecuteAsync(sql, args).GetAwaiter().GetResult();

        internal async Task<bool> ExecuteAsync(string sql, params object[] args)
        {
            try
            {
                using (var connection = await CreateConnectionAsync())
                using (var cmd = CreateSqlCommand(connection, sql, args))
                    return await cmd.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                Log.Message(LogTypes.Error, ex.ToString());

                return false;
            }
        }

        internal DbDataReader Select(string sql, params object[] args) => SelectAsync(sql, args).GetAwaiter().GetResult();

        internal async Task<DbDataReader> SelectAsync(string sql, params object[] args)
        {
            try
            {
                // Usage of an 'using' statement closes the connection too early.
                // Let the calling method dispose the command for us and close the connection with the correct CommandBehavior.
                return await CreateSqlCommand(await CreateConnectionAsync(), sql, args).ExecuteReaderAsync(CommandBehavior.CloseConnection);
            }
            catch (Exception ex)
            {
                Log.Message(LogTypes.Error, ex.ToString());

                return null;
            }
        }
    }
}
