﻿using MySql.Data.MySqlClient;
using Pchp.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using static Pchp.Library.StandardPhpOptions;
using Pchp.Library.Resources;
using System.Linq;

namespace Peachpie.Library.MySql
{
    /// <summary>
    /// MySql functions container.
    /// </summary>
    [PhpExtension("mysql", Registrator = typeof(Registrator))]
    public static partial class MySql
    {
        sealed class Registrator
        {
            public Registrator()
            {
                Context.RegisterConfiguration(new MySqlConfiguration());
                MySqlConfiguration.RegisterLegacyOptions();
            }
        }

        internal static Version EquivalentNativeLibraryVersion = new Version(7, 0, 6);

        internal const string DefaultProtocolVersion = "10";

        internal const string DefaultClientCharset = "latin1";


        #region Enums

        /// <summary>
        /// Connection flags.
        /// </summary>
        [Flags, PhpHidden]
        public enum ConnectFlags
        {
            /// <summary>
            /// No flags.
            /// </summary>
            None = 0,

            /// <summary>
            ///  Use compression protocol.
            /// </summary>
            Compress = MYSQL_CLIENT_COMPRESS,

            /// <summary>
            /// Allow space after function names.
            /// Not supported (ignored).
            /// </summary>
            IgnoreSpace = MYSQL_CLIENT_IGNORE_SPACE,

            /// <summary>
            /// Allow interactive_timeout seconds (instead of wait_timeout) of inactivity before closing the connection.
            /// Not supported (ignored).
            /// </summary>
            Interactive = MYSQL_CLIENT_INTERACTIVE,

            /// <summary>
            /// Use SSL encryption.
            /// </summary>
            SSL = MYSQL_CLIENT_SSL
        }

        /// <summary>
        /// Query result array format.
        /// </summary>
        [Flags, PhpHidden]
        public enum QueryResultKeys
        {
            /// <summary>
            /// Add items keyed by column names.
            /// </summary>
            ColumnNames = MYSQL_ASSOC,

            /// <summary>
            /// Add items keyed by column indices.
            /// </summary>
            Numbers = MYSQL_NUM,

            /// <summary>
            /// Add both items keyed by column names and items keyd by column indices.
            /// </summary>
            Both = MYSQL_BOTH
        }

        #endregion

        /// <summary>
        /// Gets last active connection.
        /// </summary>
        static MySqlConnectionResource LastConnection(Context ctx) => ctx != null ? MySqlConnectionManager.GetInstance(ctx).GetLastConnection() : null;

        static MySqlConnectionResource ValidConnection(Context ctx, PhpResource link)
        {
            var resource = link ?? LastConnection(ctx);
            if (resource is MySqlConnectionResource)
            {
                return (MySqlConnectionResource)resource;
            }
            else
            {
                PhpException.Throw(PhpError.Warning, Resources.invalid_connection_resource);
                return null;
            }
        }

        #region mysql_close

        /// <summary>
        /// Closes the non-persistent connection to the MySQL server that's associated with the specified link identifier.
        /// If link_identifier isn't specified, the last opened link is used.
        /// </summary>
        public static bool mysql_close(Context ctx, PhpResource link = null)
        {
            var connection = ValidConnection(ctx, link);
            if (connection != null)
            {
                connection.Dispose();
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion

        #region mysql_connect, mysql_pconnect

        // MySqlResource mysql_connect(string $server = ini_get("mysql.default_host")[, string $username = ini_get("mysql.default_user")[, string $password = ini_get("mysql.default_password")[, bool $new_link = false[, int $client_flags = 0]]]]] )

        /// <summary>
        /// Open a connection to a MySQL Server.
        /// </summary>
        [return: CastToFalse]
        public static PhpResource mysql_connect(Context ctx, string server = null, string username = null, string password = null, bool new_link = false, int client_flags = 0)
        {
            var config = ctx.Configuration.Get<MySqlConfiguration>();
            Debug.Assert(config != null);

            var connection_string = BuildConnectionString(config, ref server, username, password, (ConnectFlags)client_flags, characterset: "utf8mb4");

            bool success;
            var connection = MySqlConnectionManager.GetInstance(ctx)
                .CreateConnection(connection_string, new_link, -1, out success);

            if (success)
            {
                connection.Server = server;
            }
            else
            {
                connection = null;
            }

            //
            return connection;
        }

        /// <summary>
        /// Establishes a connection to MySQL server using a specified server, user, password, and flags.
        /// </summary>
        /// <returns>
        /// Resource representing the connection or a <B>null</B> reference (<B>false</B> in PHP) on failure.
        /// </returns>
        [return: CastToFalse]
        public static PhpResource mysql_pconnect(Context ctx, string server = null, string username = null, string password = null, bool new_link = false, int client_flags = 0)
        {
            // TODO: Notice: unsupported
            return mysql_connect(ctx, server, username, password, new_link, client_flags);
        }

        internal static string BuildConnectionString(MySqlConfiguration config, ref string server, string user, string password, ConnectFlags flags, int connectiontimeout = 0, string characterset = null)
        {
            // connection strings:
            if (server == null && user == null && password == null && flags == ConnectFlags.None && !string.IsNullOrEmpty(config.ConnectionString))
            {
                return config.ConnectionString;
            }

            // TODO: local.ConnectionStringName

            // build connection string:
            string pipe_name = null;
            int port = -1;

            if (server != null)
                ParseServerName(ref server, out port, out pipe_name);
            else
                server = config.Server;

            if (port == -1) port = config.Port;
            if (user == null) user = config.User;
            if (password == null) password = config.Password;

            // build the connection string to be used with MySQL Connector/.NET
            // see http://dev.mysql.com/doc/refman/5.5/en/connector-net-connection-options.html
            return BuildConnectionString(
              server, user, password,
              string.Format("allowzerodatetime=true;allow user variables=true;connect timeout={0};Port={1};SSL Mode={2};Use Compression={3}{4}{5};Max Pool Size={6}{7}{8}",
                (connectiontimeout > 0) ? connectiontimeout : (config.ConnectTimeout > 0) ? config.ConnectTimeout : 15,
                port,
                (flags & ConnectFlags.SSL) != 0 ? "Preferred" : "None",     // (since Connector 6.2.1.) ssl mode={None|Preferred|Required|VerifyCA|VerifyFull}   // (Jakub) use ssl={true|false} has been deprecated
                (flags & ConnectFlags.Compress) != 0 ? "true" : "false",    // Use Compression={true|false}
                (pipe_name != null) ? ";Pipe=" + pipe_name : null,  // Pipe={...}
                (flags & ConnectFlags.Interactive) != 0 ? ";Interactive=true" : null,    // Interactive={true|false}
                config.MaxPoolSize,                                          // Max Pool Size=100
                (config.DefaultCommandTimeout >= 0) ? ";DefaultCommandTimeout=" + config.DefaultCommandTimeout : null,
                string.IsNullOrEmpty(characterset) ? null : ";characterset="+ characterset
                )
            );
        }

        /// <summary>
		/// Builds a connection string.
		/// </summary>
		static string/*!*/ BuildConnectionString(string server, string user, string password, string additionalSettings)
        {
            var result = new StringBuilder(8);
            result.Append("server=");
            result.Append(server);
            //			result.Append(";database=");
            //			result.Append(database);
            result.Append(";user id=");
            result.Append(user);
            result.Append(";password=");
            result.Append(password);

            if (!string.IsNullOrEmpty(additionalSettings))
            {
                result.Append(';');
                result.AppendFormat(additionalSettings);
            }

            return result.ToString();
        }

        static void ParseServerName(ref string/*!*/ server, out int port, out string socketPath)
        {
            port = -1;
            socketPath = null;

            int i = server.IndexOf(':');
            if (i == -1) return;

            string port_or_socket = server.Substring(i + 1);
            server = server.Substring(0, i);

            // socket path:
            if (port_or_socket.Length > 0 && port_or_socket[0] == '/')
            {
                socketPath = port_or_socket;
            }
            else
            {

                if (!int.TryParse(port_or_socket, out port) || port < 0 || port > ushort.MaxValue)
                {
                    // PhpException.Throw(PhpError.Notice, LibResources.GetString("invalid_port", port_or_socket));
                    throw new ArgumentException(nameof(server));
                }
            }
        }

        #endregion

        #region mysql_query

        /// <summary>
        /// Sends a query to the current database associated with a specified connection.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="query">Query.</param>
        /// <param name="link">Connection resource.</param>
        /// <returns>Query resource or a <B>null</B> reference (<B>null</B> in PHP) on failure.</returns>
        [return: CastToFalse]
        public static PhpResource mysql_query(Context ctx, PhpString query, PhpResource link = null)
        {
            var connection = ValidConnection(ctx, link);
            if (connection == null || query.IsEmpty)
            {
                return null;
            }
            else if (query.ContainsBinaryData)
            {
                // be aware of binary data
                return QueryBinary(ctx.StringEncoding, query.ToBytes(ctx), connection);
            }
            else
            {
                // standard unicode behaviour
                return connection.ExecuteQuery(query.ToString(ctx), true);
            }
        }

        /// <summary>
        /// Sends a query to the current database associated with a specified connection. Preserves binary characters.
        /// </summary>
        /// <param name="encoding">Current string encoding.</param>
        /// <param name="query">Query.</param>
        /// <param name="connection">Connection resource.</param>
        /// <returns>Query resource or a <B>null</B> reference (<B>null</B> in PHP) on failure.</returns>
        internal static PhpResource QueryBinary(Encoding encoding, byte[] query, MySqlConnectionResource connection)
        {
            Debug.Assert(query != null);
            Debug.Assert(connection != null && connection.IsValid);

            //
            List<IDataParameter> parameters = null;
            string commandText = null;
            int commandTextLast = 0;

            // Parse values whether it contains non-ascii characters,
            // non-encodable values convert to byte[] parameter:
            int lastQuote = -1;
            bool escaped = false;
            bool containsNonAscii = false;  // whether encosing may corrupt value when storing into BLOB column
            int escapedChars = 0;    // amount of '\' chars (> 0 means we have to unescape the value)

            for (int i = 0; i < query.Length; i++)
            {
                byte b = query[i];

                if (b == '\'' || b == '\"')
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else //(!escaped)
                    {
                        if (lastQuote >= 0 && query[lastQuote] != b)
                            continue;   // incompatible quotes (should be escaped, but we should handle that)

                        if (lastQuote >= 0 && containsNonAscii)
                        {
                            commandText = string.Concat(commandText, encoding.GetString(query, commandTextLast, lastQuote - commandTextLast));
                            commandTextLast = i + 1;

                            // param name @paramName
                            string paramName = string.Concat("_ImplData_", i.ToString());

                            // replace [lastQuote, i] with "@paramName"
                            // and add parameter @paramName
                            byte[] value = new byte[i - lastQuote - 1 - escapedChars];
                            if (escapedChars == 0)
                            {
                                // we can block-copy the value, there are no escaped characters:
                                Buffer.BlockCopy(query, lastQuote + 1, value, 0, value.Length);
                            }
                            else
                            {
                                // unescape the value, parameters are assumed to contained raw data, escaping is not desirable:
                                UnescapeString(query, lastQuote + 1, value);
                            }

                            //
                            if (parameters == null) parameters = new List<IDataParameter>(1);
                            parameters.Add(new MySqlParameter(paramName, value));
                            commandText += '@' + paramName;

                            lastQuote = -1; // out of quoted value
                        }
                        else
                        {
                            lastQuote = i;  // start of quoted value
                            escapedChars = 0;
                        }

                        containsNonAscii = false;
                    }
                }
                else if (b > 0x7f && lastQuote >= 0)   // non-ascii character
                {
                    // this character may not pass:
                    containsNonAscii = true;
                    escaped = false;
                }
                else if (escaped)
                {
                    escaped = false;
                }
                else if (b == '\\') // && !escaped)
                {
                    escapedChars++;
                    escaped = true;
                }
                // handle comments (only outside quoted values):
                else if (lastQuote < 0)
                {
                    // escaped = false
                    // 
                    byte bnext = ((i + 1) < query.Length) ? query[i + 1] : (byte)0;
                    if (b == '/' && bnext == '*') // /*
                    {
                        // /* comment */
                        i += 2;
                        while ((i + 1) < query.Length && (query[i] != '*' || query[i + 1] != '/'))
                            i++;    // skip comment
                    }
                    else if (   // -- or #
                        (b == '-' && bnext == '-' && (i + 2 < query.Length) && char.IsWhiteSpace((char)query[i + 2])) ||
                        (b == '#'))
                    {
                        // single line comment
                        i++;
                        while (i < query.Length && query[i] != '\n')
                            i++;
                    }
                }
            }

            //
            commandText = string.Concat(commandText, encoding.GetString(query, commandTextLast, query.Length - commandTextLast));
            return connection.ExecuteCommand(commandText, CommandType.Text, true, parameters, false);
        }

        /// <summary>
        /// Inverse function to <see cref="mysql_escape_string(Context, PhpString)"/>.
        /// </summary>
        /// <param name="source">Source byte array containing escaped characters.</param>
        /// <param name="startFrom">Index of the first character to start unescaping with.</param>
        /// <param name="dest">Target byte array where unescaped <paramref name="source"/> is copied.</param>
        /// <remarks>This method unescapes as many characters as <paramref name="dest"/> can hold.</remarks>
        private static void UnescapeString(byte[]/*!*/source, int startFrom, byte[]/*!*/dest)
        {
            Debug.Assert(source != null);
            Debug.Assert(dest != null);
            Debug.Assert(startFrom >= 0 && startFrom < source.Length);
            Debug.Assert(source.Length - startFrom /* - escapedChars */ >= dest.Length);

            int dest_index = 0; // dest write index
            int source_length = source.Length;

            for (int i = startFrom; dest_index < dest.Length; i++)
            {
                byte b = source[i];

                // unescape the character (invert function to EscapeString)
                if (b == '\\')// && i < source_length - 1)
                {
                    Debug.Assert(i < source_length - 1);

                    b = source[++i];    // next char after the \
                    if (b == 'n') b = (byte)'\n';
                    else if (b == 'r') b = (byte)'\r';
                    else if (b == 'Z') b = (byte)'\u001a';
                    // else: other characters are as they are
                }

                //
                dest[dest_index++] = b;
            }
        }

        #endregion

        #region mysql_insert_id, mysql_thread_id

        /// <summary>
        /// Gets id generated by the previous insert operation.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns>Id or 0 if the last command wasn't an insertion.</returns>
        [return: CastToFalse]
        public static long mysql_insert_id(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            return connection != null ? connection.LastInsertedId : -1;
        }

        /// <summary>
        /// Gets the current DB thread's id.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns>The thread id.</returns>
        public static int mysql_thread_id(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            if (connection == null) return 0;

            return connection.ServerThread;
        }

        #endregion

        #region mysql_fetch_row, mysql_fetch_assoc, mysql_fetch_array, mysql_fetch_object

        /// <summary>
        /// Get a result row as an integer indexed array. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>Array indexed by integers starting from 0 containing values of the current row.</returns>
        [return: CastToFalse]
        public static PhpArray mysql_fetch_row(PhpResource resultHandle)
        {
            return mysql_fetch_array(resultHandle, QueryResultKeys.Numbers);
        }

        /// <summary>
        /// Get a result row as an associative array. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>Array indexed by column names containing values of the current row.</returns>
        [return: CastToFalse]
        public static PhpArray mysql_fetch_assoc(PhpResource resultHandle)
        {
            return mysql_fetch_array(resultHandle, QueryResultKeys.ColumnNames);
        }

        /// <summary>
        /// Get a result row as an array with a specified key format. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="resultType">Type(s) of keys in the resulting array.</param>
        /// <returns>
        /// Array containing values of the rows indexed by column keys and/or column indices depending 
        /// on value of <paramref name="resultType"/>.
        /// </returns>
        [return: CastToFalse]
        public static PhpArray mysql_fetch_array(PhpResource resultHandle, QueryResultKeys resultType = QueryResultKeys.Both)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            switch (resultType)
            {
                case QueryResultKeys.ColumnNames: return result.FetchArray(false, true);
                case QueryResultKeys.Numbers: return result.FetchArray(true, false);
                case QueryResultKeys.Both: return result.FetchArray(true, true);
            }

            return null;
        }

        /// <summary>
        /// Get a result row as an object. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>
        /// Object whose fields contain values from the current row. 
        /// Field names corresponds to the column names.
        /// </returns>
        [return: CastToFalse]
        public static object mysql_fetch_object(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            return result.FetchStdClass();
        }

        #endregion

        #region mysql_affected_rows

        /// <summary>
        /// Get a number of affected rows in the previous operation.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns>The number of affected rows or -1 if the last operation failed or the connection is invalid.</returns>
        public static int mysql_affected_rows(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            if (connection == null || connection.LastException != null) return -1;

            return connection.LastAffectedRows;
        }

        #endregion

        #region mysql_num_fields, mysql_num_rows

        /// <summary>
        /// Get number of columns (fields) in a specified result.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>Number of columns in the specified result or 0 if the result resource is invalid.</returns>
        public static int mysql_num_fields(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return 0;

            return result.FieldCount;
        }

        /// <summary>
        /// Get number of rows in a specified result.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>Number of rows in the specified result or 0 if the result resource is invalid.</returns>
        public static int mysql_num_rows(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return 0;

            return result.RowCount;
        }

        #endregion

        #region mysql_free_result

        /// <summary>
        /// Releases a resource represening a query result.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns><B>true</B> on success, <B>false</B> on failure (invalid resource).</returns>
        public static bool mysql_free_result(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result != null)
            {
                result.Dispose();
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion

        #region mysql_select_db

        /// <summary>
        /// Selects the current DB for a specified connection.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="databaseName">Name of the database.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns><B>true</B> on success, <B>false</B> on failure.</returns>
        public static bool mysql_select_db(Context ctx, string databaseName, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            return connection != null && connection.SelectDb(databaseName);
        }

        #endregion

        #region mysql_error, mysql_errno

        /// <summary>
        /// Returns the text of the error message from previous operation.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns>
        /// Error message, empty string if no error occured, or a <B>null</B> reference 
        /// if the connection resource is invalid.
        /// </returns>
        public static string mysql_error(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            if (connection == null)
            {
                return null;
            }

            return connection.GetLastErrorMessage();
        }

        /// <summary>
        /// Returns the number of the error from previous operation.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="linkIdentifier">Connection resource.</param>
        /// <returns>Error number, 0 if no error occured, or -1 if the number cannot be retrieved.</returns>
        public static int mysql_errno(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            return (connection != null) ? connection.GetLastErrorNumber() : -1;
        }

        #endregion

        #region mysql_result

        /// <summary>
        /// Gets a contents of a specified cell from a specified query result resource.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="row">Row index.</param>
        /// <param name="field">Column (field) integer index or string name.</param>
        /// <returns>The value of the cell or a <B>null</B> reference (<B>false</B> in PHP) on failure (invalid resource or row/field index/name).</returns>
        /// <remarks>
        /// Result is affected by run-time quoting.
        /// </remarks>
        public static PhpValue mysql_result(PhpResource resultHandle, int row, PhpValue field)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null)
            {
                return PhpValue.False;
            }

            string field_name;
            object field_value;
            if (field.IsEmpty)
            {
                field_value = result.GetFieldValue(row, result.CurrentFieldIndex);
            }
            else if ((field_name = PhpVariable.AsString(field)) != null)
            {
                field_value = result.GetFieldValue(row, field_name);
            }
            else
            {
                field_value = result.GetFieldValue(row, (int)field.ToLong());
            }

            return PhpValue.FromClr(field_value); // TODO: Core.Convert.Quote(field_value, context);
        }

        #endregion

        #region mysql_field_name, mysql_field_type, mysql_field_len

        /// <summary>
        /// Gets a name of a specified column (field) in a result. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Column (field) index.</param>
        /// <returns>Name of the column or a <B>null</B> reference on failure (invalid resource or column index).</returns>
        public static string mysql_field_name(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            return result.GetFieldName(fieldIndex);
        }

        /// <summary>
        /// Gets a type of a specified column (field) in a result. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Column index.</param>
        /// <returns>MySQL type translated to PHP terminology.</returns>
        /// <remarks>
        /// Possible values are: "string", "int", "real", "year", "date", "timestamp", "datetime", "time", 
        /// "set", "enum", "blob", "bit" (Phalanger specific), "NULL", and "unknown".
        /// </remarks>
        public static string mysql_field_type(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            return result.GetPhpFieldType(fieldIndex);
        }

        /// <summary>
        /// Gets a length of a specified column (field) in a result. 
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Column index.</param>
        /// <returns>Length of the column or a -1 on failure (invalid resource or column index).</returns>
        public static int mysql_field_len(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return -1;

            return result.GetFieldLength(fieldIndex);
        }

        #endregion

        #region mysql_field_seek, mysql_data_seek

        /// <summary>
        /// Sets the result resource's current column (field) offset.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldOffset">New column offset.</param>
        /// <returns><B>true</B> on success, <B>false</B> on failure (invalid resource or column offset).</returns>
        public static bool mysql_field_seek(PhpResource resultHandle, int fieldOffset)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            return result != null && result.SeekField(fieldOffset);
        }

        /// <summary>
        /// Sets the result resource's current row index.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="rowIndex">New row index.</param>
        /// <returns><B>true</B> on success, <B>false</B> on failure (invalid resource or row index).</returns>
        public static bool mysql_data_seek(PhpResource resultHandle, int rowIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            return result != null && result.SeekRow(rowIndex);
        }

        #endregion

        #region mysql_field_table, mysql_field_flags, mysql_fetch_field, mysql_fetch_lengths

        /// <summary>
        /// Gets a base table of a specified field.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Field index.</param>
        /// <returns>Name of the base table of the field.</returns>
        public static string mysql_field_table(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            //var info = result.GetSchemaRowInfo(fieldIndex);
            //if (info == null) return null;

            //return (string)info["BaseTableName"];
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets flags of a specified field.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Field index.</param>
        /// <returns>Flags of the field.</returns>
        public static string mysql_field_flags(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            throw new NotImplementedException();

            //ColumnFlags flags = result.GetFieldFlags(fieldIndex);
            //string type_name = result.GetPhpFieldType(fieldIndex);

            //StringBuilder str_fields = new StringBuilder();

            //if ((flags & ColumnFlags.NOT_NULL) != 0)
            //    str_fields.Append("not_null ");

            //if ((flags & ColumnFlags.PRIMARY_KEY) != 0)
            //    str_fields.Append("primary_key ");

            //if ((flags & ColumnFlags.UNIQUE_KEY) != 0)
            //    str_fields.Append("unique_key ");

            //if ((flags & ColumnFlags.MULTIPLE_KEY) != 0)
            //    str_fields.Append("multiple_key ");

            //if ((flags & ColumnFlags.BLOB) != 0)
            //    str_fields.Append("blob ");

            //if ((flags & ColumnFlags.UNSIGNED) != 0)
            //    str_fields.Append("unsigned ");

            //if ((flags & ColumnFlags.ZERO_FILL) != 0)
            //    str_fields.Append("zerofill ");

            //if ((flags & ColumnFlags.BINARY) != 0)
            //    str_fields.Append("binary ");

            //if ((flags & ColumnFlags.ENUM) != 0)
            //    str_fields.Append("enum ");

            //if ((flags & ColumnFlags.SET) != 0)
            //    str_fields.Append("set ");

            //if ((flags & ColumnFlags.AUTO_INCREMENT) != 0)
            //    str_fields.Append("auto_increment ");

            //if ((flags & ColumnFlags.TIMESTAMP) != 0)
            //    str_fields.Append("timestamp ");

            //if (str_fields.Length > 0)
            //    str_fields.Length = str_fields.Length - 1;

            //return str_fields.ToString();
        }

        /// <summary>
        /// Gets a PHP object whose properties describes the last fetched field.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>The PHP object.</returns>
        public static object mysql_fetch_field(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            return FetchFieldInternal(result, result.FetchNextField());
        }

        /// <summary>
        /// Gets a PHP object whose properties describes a specified field.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <param name="fieldIndex">Field index.</param>
        /// <returns>The PHP object.</returns>
        public static stdClass mysql_fetch_field(PhpResource resultHandle, int fieldIndex)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            return FetchFieldInternal(result, fieldIndex);
        }

        static stdClass FetchFieldInternal(MySqlResultResource/*!*/ result, int fieldIndex)
        {
            if (!result.CheckFieldIndex(fieldIndex))
                return null;

            //DataRow info = result.GetSchemaRowInfo(fieldIndex);
            //if (info == null) return null;

            Debug.Assert(result.GetRowCustomData() != null);

            string php_type = result.GetPhpFieldType(fieldIndex);
            //PhpMyDbResult.FieldCustomData data = ((PhpMyDbResult.FieldCustomData[])result.GetRowCustomData())[fieldIndex];
            //ColumnFlags flags = data.Flags;//result.GetFieldFlags(fieldIndex);

            // create an array of runtime fields with specified capacity:
            var objFields = new PhpArray(13);

            // add fields into the hastable directly:
            // no duplicity check, since array is already valid
            objFields.Add("name", result.GetFieldName(fieldIndex));
            //objFields.Add("table", (/*info["BaseTableName"] as string*/data.RealTableName) ?? string.Empty);
            //objFields.Add("def", ""); // TODO
            //objFields.Add("max_length", /*result.GetFieldLength(fieldIndex)*/data.ColumnSize);

            //objFields.Add("not_null", ((flags & ColumnFlags.NOT_NULL) != 0) /*(!(bool)info["AllowDBNull"])*/ ? 1 : 0);
            //objFields.Add("primary_key", ((flags & ColumnFlags.PRIMARY_KEY) != 0) /*((bool)info["IsKey"])*/ ? 1 : 0);
            //objFields.Add("multiple_key", ((flags & ColumnFlags.MULTIPLE_KEY) != 0) /*((bool)info["IsMultipleKey"])*/ ? 1 : 0);
            //objFields.Add("unique_key", ((flags & ColumnFlags.UNIQUE_KEY) != 0) /*((bool)info["IsUnique"])*/ ? 1 : 0);
            //objFields.Add("numeric", result.IsNumericType(php_type) ? 1 : 0);
            //objFields.Add("blob", ((flags & ColumnFlags.BLOB) != 0) /*((bool)info["IsBlob"])*/ ? 1 : 0);

            //objFields.Add("type", php_type);
            //objFields.Add("unsigned", ((flags & ColumnFlags.UNSIGNED) != 0) /*((bool)info["IsUnsigned"])*/ ? 1 : 0);
            //objFields.Add("zerofill", ((flags & ColumnFlags.ZERO_FILL) != 0) /*((bool)info["ZeroFill"])*/ ? 1 : 0);

            // create new stdClass with runtime fields initialized above:
            return (stdClass)objFields.ToClass();
        }

        /// <summary>
        /// Gets an array of lengths of the values of the current row.
        /// </summary>
        /// <param name="resultHandle">Query result resource.</param>
        /// <returns>An array containing a length of each value of the current row.</returns>
        [return: CastToFalse]
        public static PhpArray mysql_fetch_lengths(PhpResource resultHandle)
        {
            var result = MySqlResultResource.ValidResult(resultHandle);
            if (result == null) return null;

            int row_index = result.CurrentRowIndex;
            if (row_index < 0) return null;

            var array = new PhpArray(result.FieldCount);

            for (int i = 0; i < result.FieldCount; i++)
            {
                object value = result.GetFieldValue(row_index, i);

                if (value is PhpString phpstr)
                    array.Add(phpstr.Length);
                else if (value != null)
                    array.Add(value.ToString().Length);
                else
                    array.Add(0);
            }

            return array;
        }

        #endregion

        #region mysql_get_client_info, mysql_get_server_info, mysql_get_host_info, mysql_get_proto_info

        /// <summary>
        /// Gets a version of the client library.
        /// </summary>
        /// <returns>Equivalent native library varsion.</returns>
        public static string mysql_get_client_info() => EquivalentNativeLibraryVersion.ToString();

        /// <summary>
        /// Gets server version.
        /// </summary>
        /// <returns>Server version</returns>
        public static string mysql_get_server_info(Context ctx) => mysql_get_server_info(LastConnection(ctx) ?? mysql_connect(ctx));

        /// <summary>
        /// Gets server version.
        /// </summary>
        /// <returns>Server version</returns>
        public static string mysql_get_server_info(PhpResource link)
        {
            var connection = ValidConnection(null, link);
            if (connection == null) return null;

            return connection.ServerVersion;
        }

        /// <summary>
        /// Gets information about the server.
        /// </summary>
        /// <returns>Server name and protocol type.</returns>
        public static string mysql_get_host_info(Context ctx) => mysql_get_host_info(LastConnection(ctx) ?? mysql_connect(ctx));

        /// <summary>
        /// Gets information about the server.
        /// </summary>
        /// <param name="link">Connection resource.</param>
        /// <returns>Server name and protocol type.</returns>
        public static string mysql_get_host_info(PhpResource link)
        {
            var connection = ValidConnection(null, link);
            if (connection == null) return null;

            return string.Concat(connection.Server, " via TCP/IP"); // TODO: how to get the protocol?
        }

        /// <summary>
        /// Gets version of the protocol.
        /// </summary>
        /// <returns>Protocol version.</returns>
        public static string mysql_get_proto_info(Context ctx) => mysql_get_proto_info(LastConnection(ctx) ?? mysql_connect(ctx));

        /// <summary>
        /// Gets version of the protocol.
        /// </summary>
        /// <param name="link">Connection resource.</param>
        /// <returns>Protocol version.</returns>
        public static string mysql_get_proto_info(PhpResource link)
        {
            var connection = ValidConnection(null, link);
            if (connection == null) return null;

            object value = connection.QueryGlobalVariable("protocol_version");
            return (value != null) ? value.ToString() : DefaultProtocolVersion;
        }

        #endregion

        #region mysql_real_escape_string, mysql_escape_string

        /// <summary>
        /// Escapes special characters in a string for use in a SQL statement.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="str">String to escape.</param>
        /// <param name="link">Connection resource.</param>
        /// <returns>Escaped string.</returns>
        [return: CastToFalse]
        public static PhpString mysql_real_escape_string(Context ctx, PhpString str, PhpResource link = null)
        {
            var connection = ValidConnection(ctx, link);
            if (connection == null)
            {
                // TODO: create default connection
            }

            // TODO: get character set from connection

            return mysql_escape_string(ctx, str);
        }

        /// <summary>
        /// Escapes special characters in a string for use in a SQL statement.
        /// </summary>
        /// <param name="ctx">Runtime context.</param>
        /// <param name="unescaped_str">String to escape.</param>
        /// <returns>Escaped string.</returns>
        public static PhpString mysql_escape_string(Context ctx, PhpString unescaped_str)
        {
            if (unescaped_str.IsEmpty)
            {
                return PhpString.Empty;
            }

            // binary aware:
            if (unescaped_str.ContainsBinaryData)
            {
                var bytes = unescaped_str.ToBytes(ctx);
                if (bytes.Length == 0) return unescaped_str;

                List<byte>/*!*/result = new List<byte>(bytes.Length + 8);
                for (int i = 0; i < bytes.Length; i++)
                {
                    switch (bytes[i])
                    {
                        case (byte)'\0': result.Add((byte)'\\'); goto default;
                        case (byte)'\\': result.Add((byte)'\\'); goto default;
                        case (byte)'\n': result.Add((byte)'\\'); result.Add((byte)'n'); break;
                        case (byte)'\r': result.Add((byte)'\\'); result.Add((byte)'r'); break;
                        case (byte)'\u001a': result.Add((byte)'\\'); result.Add((byte)'Z'); break;
                        case (byte)'\'': result.Add((byte)'\\'); goto default;
                        case (byte)'"': result.Add((byte)'\\'); goto default;
                        default: result.Add(bytes[i]); break;
                    }
                }

                return new PhpString(result.ToArray());
            }

            // else
            return new PhpString(MySqlHelper.EscapeString(unescaped_str.ToString(ctx)));
        }

        #endregion

        #region mysql_client_encoding, mysql_set_charset

        /// <summary>
        /// Gets the name of the client character set.
        /// </summary>
        /// <returns>Character set name.</returns>
        public static string mysql_client_encoding(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            if (connection == null) return null;

            object value = connection.QueryGlobalVariable("character_set_client");
            return (value != null) ? value.ToString() : DefaultClientCharset;
        }

        internal static bool MysqlValidateCharset(string charset)
        {
            if (string.IsNullOrEmpty(charset))
            {
                return false;
            }

            // validate the charset (only a-z, 0-9, _ allowed, see mysqlnd_find_charset_name):
            for (int i = 0; i < charset.Length; i++)
            {
                var c = charset[i];

                if ((c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    (c == '_'))
                {
                    continue;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sets the client character set.
        /// </summary>
        /// <param name="charset">New character set. Must be valid SQL character set.</param>
        /// <param name="ctx"></param>
        /// <param name="linkIdentifier"></param>
        /// <returns>True if successful.</returns>
        public static bool mysql_set_charset(Context ctx, string charset, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            if (connection == null) return false;

            if (!MysqlValidateCharset(charset))
            {
                PhpException.InvalidArgument(nameof(charset));
                return false;
            }

            // set the charset:
            var result = connection.ExecuteCommand("SET NAMES " + charset, CommandType.Text, false, null, true);
            if (result != null) result.Dispose();

            // success if there were no errors:
            return connection.LastException == null;
        }

        #endregion

        #region mysql_ping

        /// <summary>
        /// Ping a server connection or reconnect if there is no connection.
        /// </summary>
        /// <returns>Whether the connection to the server MySQL server is working.</returns>
        public static bool mysql_ping(Context ctx, PhpResource linkIdentifier = null)
        {
            var connection = ValidConnection(ctx, linkIdentifier);
            try
            {
                return connection != null && connection.Ping();
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
