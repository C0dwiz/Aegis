using Aegis.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations
{
    [DbContext(typeof(AegisDbContext))]
    [Migration("20260401073500_RepairMissingUserProfileColumns")]
    public class RepairMissingUserProfileColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"BirthDate\" date;");

            migrationBuilder.Sql(
                "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Location\" text;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Users\" DROP COLUMN IF EXISTS \"BirthDate\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"Users\" DROP COLUMN IF EXISTS \"Location\";");
        }
    }
}