using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FransStudios.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInquiryOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Origin",
                table: "Inquiries",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1); // InquiryOrigin.Packages

            // Existing requests: the Packages form asks for coverage, the Contact form doesn't.
            migrationBuilder.Sql("UPDATE Inquiries SET Origin = 2 WHERE Coverage IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Inquiries");
        }
    }
}
