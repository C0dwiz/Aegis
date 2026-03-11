using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Data.Migrations
{
    /// <inheritdoc />
    public partial class HotPathIndexesPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_PrivateChats_User2Id",
                table: "PrivateChats");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_UserId",
                table: "GroupMembers");

            migrationBuilder.DropIndex(
                name: "IX_ChannelMembers_UserId",
                table: "ChannelMembers");

            migrationBuilder.DropIndex(
                name: "IX_BotTokens_BotId",
                table: "BotTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ConnectionId_IsActive",
                table: "Sessions",
                columns: new[] { "ConnectionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId_IsActive",
                table: "Sessions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateChats_User1Id_IsActive_LastActivityAt",
                table: "PrivateChats",
                columns: new[] { "User1Id", "IsActive", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateChats_User2Id_IsActive_LastActivityAt",
                table: "PrivateChats",
                columns: new[] { "User2Id", "IsActive", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_FromUserId_ToUserId_IsDeleted_CreatedAt",
                table: "Messages",
                columns: new[] { "FromUserId", "ToUserId", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ToUserId_IsDelivered_IsDeleted_CreatedAt",
                table: "Messages",
                columns: new[] { "ToUserId", "IsDelivered", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ToUserId_IsRead_IsDeleted_FromUserId",
                table: "Messages",
                columns: new[] { "ToUserId", "IsRead", "IsDeleted", "FromUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMessages_GroupId_IsDeleted_CreatedAt",
                table: "GroupMessages",
                columns: new[] { "GroupId", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId_IsActive",
                table: "GroupMembers",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMessages_ChannelId_IsDeleted_CreatedAt",
                table: "ChannelMessages",
                columns: new[] { "ChannelId", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMembers_UserId_IsActive",
                table: "ChannelMembers",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BotTokens_BotId_RevokedAt_CreatedAt",
                table: "BotTokens",
                columns: new[] { "BotId", "RevokedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_ConnectionId_IsActive",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_UserId_IsActive",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_PrivateChats_User1Id_IsActive_LastActivityAt",
                table: "PrivateChats");

            migrationBuilder.DropIndex(
                name: "IX_PrivateChats_User2Id_IsActive_LastActivityAt",
                table: "PrivateChats");

            migrationBuilder.DropIndex(
                name: "IX_Messages_FromUserId_ToUserId_IsDeleted_CreatedAt",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ToUserId_IsDelivered_IsDeleted_CreatedAt",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ToUserId_IsRead_IsDeleted_FromUserId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_GroupMessages_GroupId_IsDeleted_CreatedAt",
                table: "GroupMessages");

            migrationBuilder.DropIndex(
                name: "IX_GroupMembers_UserId_IsActive",
                table: "GroupMembers");

            migrationBuilder.DropIndex(
                name: "IX_ChannelMessages_ChannelId_IsDeleted_CreatedAt",
                table: "ChannelMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChannelMembers_UserId_IsActive",
                table: "ChannelMembers");

            migrationBuilder.DropIndex(
                name: "IX_BotTokens_BotId_RevokedAt_CreatedAt",
                table: "BotTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateChats_User2Id",
                table: "PrivateChats",
                column: "User2Id");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId",
                table: "GroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMembers_UserId",
                table: "ChannelMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BotTokens_BotId",
                table: "BotTokens",
                column: "BotId");
        }
    }
}
