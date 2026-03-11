using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations;

[Migration("20260311095000_FixUsersIsActiveBooleanForPostgres")]
public partial class FixUsersIsActiveBooleanForPostgres : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'Users'
                  AND column_name = 'IsActive'
                  AND data_type IN ('smallint', 'integer', 'bigint')
              ) THEN
                ALTER TABLE "Users"
                  ALTER COLUMN "IsActive" TYPE boolean
                  USING CASE
                    WHEN "IsActive" IS NULL THEN FALSE
                    ELSE "IsActive" <> 0
                  END;
              END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'Users'
                  AND column_name = 'IsActive'
                  AND data_type = 'boolean'
              ) THEN
                ALTER TABLE "Users"
                  ALTER COLUMN "IsActive" TYPE integer
                  USING CASE
                    WHEN "IsActive" THEN 1
                    ELSE 0
                  END;
              END IF;
            END $$;
            """);
    }
}
