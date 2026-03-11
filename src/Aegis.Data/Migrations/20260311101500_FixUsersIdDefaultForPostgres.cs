using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations;

[Migration("20260311101500_FixUsersIdDefaultForPostgres")]
public partial class FixUsersIdDefaultForPostgres : Migration
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
                  AND column_name = 'Id'
              ) THEN
                CREATE SEQUENCE IF NOT EXISTS "Users_Id_seq";

                ALTER SEQUENCE "Users_Id_seq" OWNED BY "Users"."Id";

                PERFORM setval(
                  '"Users_Id_seq"',
                  COALESCE((SELECT MAX("Id")::bigint FROM "Users"), 0) + 1,
                  false
                );

                ALTER TABLE "Users"
                  ALTER COLUMN "Id" SET DEFAULT nextval('"Users_Id_seq"');
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
                  AND column_name = 'Id'
              ) THEN
                ALTER TABLE "Users"
                  ALTER COLUMN "Id" DROP DEFAULT;
              END IF;
            END $$;
            """);
    }
}
