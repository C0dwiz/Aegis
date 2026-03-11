using System;
using Aegis.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aegis.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AegisDbContext))]
    [Migration("20260311075125_UserAvatarsAndChannelAliases")]
    public partial class UserAvatarsAndChannelAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicAlias",
                table: "Channels",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserAvatars",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<ulong>(type: "bigint", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAvatars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAvatars_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_PublicAlias",
                table: "Channels",
                column: "PublicAlias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAvatars_UserId_IsPrimary",
                table: "UserAvatars",
                columns: new[] { "UserId", "IsPrimary" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAvatars");

            migrationBuilder.DropIndex(
                name: "IX_Channels_PublicAlias",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PublicAlias",
                table: "Channels");
        }
    }
}
