using MySqlConnector;

namespace BlazorStoc.Services;

public static class MariaArchiveSchema
{
    public const int Version = 6;

    public static async Task InitializeAsync(IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var statement in Statements)
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        // archive_stock_movements created by an earlier version gains the destination and vehicle columns.
        foreach (var (column, definition) in new[] { ("destination", "TINYINT NULL"), ("vehicle_id", "INT NULL"), ("source_vehicle_id", "INT NULL") })
        {
            await using var exists = new MySqlCommand("""
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='archive_stock_movements' AND COLUMN_NAME=@column
                """, connection);
            exists.Parameters.AddWithValue("@column", column);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0) continue;
            await using var alter = new MySqlCommand($"ALTER TABLE archive_stock_movements ADD COLUMN {column} {definition}", connection);
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS archive_schema_metadata (
            schema_key VARCHAR(100) NOT NULL PRIMARY KEY,
            schema_value VARCHAR(100) NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS audit_events (
            id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            timestamp_utc DATETIME(6) NOT NULL,
            actor_username VARCHAR(191) NOT NULL,
            actor_role VARCHAR(100) NOT NULL,
            entity_type VARCHAR(100) NOT NULL,
            action VARCHAR(100) NOT NULL,
            target VARCHAR(500) NOT NULL,
            details LONGTEXT NOT NULL,
            motif VARCHAR(1000) NOT NULL DEFAULT '',
            entity_id VARCHAR(128) NOT NULL DEFAULT '',
            archive_operation_id CHAR(36) CHARACTER SET ascii NULL,
            INDEX ix_audit_events_timestamp(timestamp_utc),
            INDEX ix_audit_events_archive_operation(archive_operation_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_operations (
            id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            entity_type VARCHAR(100) NOT NULL,
            original_id VARCHAR(128) NOT NULL,
            original_version BIGINT UNSIGNED NOT NULL,
            deleted_utc DATETIME(6) NOT NULL,
            actor_username VARCHAR(191) NOT NULL,
            actor_role VARCHAR(100) NOT NULL,
            motif VARCHAR(1000) NOT NULL,
            target VARCHAR(500) NOT NULL,
            details LONGTEXT NOT NULL,
            data_json LONGTEXT NOT NULL,
            protected_data_json LONGTEXT NULL,
            INDEX ix_archive_operations_object(entity_type,original_id),
            INDEX ix_archive_operations_deleted(deleted_utc),
            INDEX ix_archive_operations_actor(actor_username,deleted_utc),
            CONSTRAINT ck_archive_operations_data_json CHECK (JSON_VALID(data_json)),
            CONSTRAINT ck_archive_operations_protected_json CHECK (protected_data_json IS NULL OR JSON_VALID(protected_data_json))
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_products (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            category VARCHAR(200) NOT NULL,
            subcategory VARCHAR(200) NOT NULL,
            name VARCHAR(200) NOT NULL,
            description TEXT NOT NULL,
            quantity INT NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            INDEX ix_archive_products_original(original_id),
            CONSTRAINT fk_archive_products_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_beneficiaries (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            cui VARCHAR(20) NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            INDEX ix_archive_beneficiaries_original(original_id),
            CONSTRAINT fk_archive_beneficiaries_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_vehicles (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            plate_number VARCHAR(12) NOT NULL,
            description VARCHAR(100) NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            INDEX ix_archive_vehicles_original(original_id),
            CONSTRAINT fk_archive_vehicles_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_web_users (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            username VARCHAR(191) NOT NULL,
            display_name VARCHAR(200) NOT NULL,
            role VARCHAR(100) NOT NULL,
            is_active BOOLEAN NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            password_hash TEXT NOT NULL,
            INDEX ix_archive_web_users_original(original_id),
            CONSTRAINT fk_archive_web_users_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_projects (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            beneficiary_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            observations TEXT NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            INDEX ix_archive_projects_original(original_id),
            CONSTRAINT fk_archive_projects_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_project_observations (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            project_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            content TEXT NOT NULL,
            author VARCHAR(191) NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            INDEX ix_archive_project_observations_original(original_id),
            CONSTRAINT fk_archive_project_observations_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_project_observation_files (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            observation_id INT NOT NULL,
            original_name VARCHAR(500) NOT NULL,
            content_type VARCHAR(255) NOT NULL,
            byte_length BIGINT UNSIGNED NOT NULL,
            sha256 CHAR(64) NOT NULL,
            author VARCHAR(191) NOT NULL,
            uploaded_utc DATETIME(6) NOT NULL,
            INDEX ix_archive_project_observation_files_original(original_id),
            CONSTRAINT fk_archive_project_observation_files_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_stock_movements (
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL PRIMARY KEY,
            original_id INT NOT NULL,
            product_id INT NOT NULL,
            kind TINYINT NOT NULL,
            quantity INT NOT NULL,
            movement_date CHAR(10) NOT NULL,
            description TEXT NOT NULL,
            beneficiary_id INT NULL,
            project_id INT NULL,
            operator VARCHAR(191) NOT NULL,
            version BIGINT UNSIGNED NOT NULL,
            destination TINYINT NULL,
            vehicle_id INT NULL,
            source_vehicle_id INT NULL,
            INDEX ix_archive_stock_movements_original(original_id),
            CONSTRAINT fk_archive_stock_movements_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_relations (
            id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL,
            relation_type VARCHAR(100) NOT NULL,
            original_relation_id VARCHAR(128) NOT NULL,
            data_json LONGTEXT NOT NULL,
            UNIQUE KEY ux_archive_relations_identity(archive_id,relation_type,original_relation_id),
            INDEX ix_archive_relations_object(relation_type,original_relation_id),
            CONSTRAINT fk_archive_relations_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT,
            CONSTRAINT ck_archive_relations_data_json CHECK (JSON_VALID(data_json))
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        """
        CREATE TABLE IF NOT EXISTS archive_files (
            id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
            archive_id CHAR(36) CHARACTER SET ascii NOT NULL,
            relation_type VARCHAR(100) NOT NULL,
            original_relation_id VARCHAR(128) NOT NULL,
            live_relative_path VARCHAR(1000) NOT NULL,
            archive_relative_path VARCHAR(1000) NOT NULL,
            content_type VARCHAR(255) NOT NULL,
            file_name VARCHAR(500) NOT NULL,
            byte_length BIGINT UNSIGNED NOT NULL,
            content_hash VARCHAR(128) NOT NULL,
            archived_utc DATETIME(6) NOT NULL,
            UNIQUE KEY ux_archive_files_identity(archive_id,relation_type,original_relation_id),
            INDEX ix_archive_files_object(relation_type,original_relation_id),
            CONSTRAINT fk_archive_files_operation FOREIGN KEY(archive_id) REFERENCES archive_operations(id) ON DELETE RESTRICT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
        $"""
        INSERT INTO archive_schema_metadata(schema_key,schema_value)
        VALUES('schema_version','{Version}')
        ON DUPLICATE KEY UPDATE schema_value=VALUES(schema_value)
        """
    ];
}
