using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolAdmission.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admission_classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admission_classes", x => x.id);
                    table.CheckConstraint("ck_admission_classes_total_seats_positive", "total_seats > 0");
                });

            migrationBuilder.CreateTable(
                name: "admission_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "character(17)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admission_holds", x => x.id);
                    table.CheckConstraint("ck_admission_holds_cancelled_at_matches_status", "(status = 'CANCELLED') = (cancelled_at IS NOT NULL)");
                    table.CheckConstraint("ck_admission_holds_confirmed_at_matches_status", "(status = 'CONFIRMED') = (confirmed_at IS NOT NULL)");
                    table.CheckConstraint("ck_admission_holds_expires_after_created", "expires_at > created_at");
                    table.CheckConstraint("ck_admission_holds_status_valid", "status IN ('HELD','CONFIRMED','EXPIRED','CANCELLED')");
                    table.CheckConstraint("ck_admission_holds_student_id_format", "student_id ~ '^[0-9]{17}$'");
                    table.ForeignKey(
                        name: "fk_admission_holds_admission_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "admission_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_class_id",
                table: "admission_holds",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_class_status_expires",
                table: "admission_holds",
                columns: new[] { "class_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_expires_at",
                table: "admission_holds",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_status",
                table: "admission_holds",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_admission_holds_one_application_per_class",
                table: "admission_holds",
                columns: new[] { "class_id", "student_id" },
                unique: true,
                filter: "status IN ('HELD','CONFIRMED','CANCELLED')");

            migrationBuilder.CreateIndex(
                name: "ux_admission_holds_one_held_per_student",
                table: "admission_holds",
                column: "student_id",
                unique: true,
                filter: "status = 'HELD'");

            migrationBuilder.Sql("CREATE INDEX ix_admission_holds_student_id ON admission_holds (student_id);");

            // Case-insensitive unique class name (EF cannot model expression indexes).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_admission_classes_name_lower ON admission_classes (lower(name));");

            // Defense in depth for "held + confirmed <= total_seats" (primary protection is the
            // class-row lock taken by the application). Checked only when a row *enters* an active
            // state; HELD -> CONFIRMED does not change the active count, so it is not re-checked.
            migrationBuilder.Sql("""
                CREATE FUNCTION fn_admission_holds_enforce_capacity() RETURNS trigger AS $$
                DECLARE
                    v_total  integer;
                    v_active integer;
                BEGIN
                    IF NEW.status NOT IN ('HELD', 'CONFIRMED') THEN
                        RETURN NEW;
                    END IF;
                    IF TG_OP = 'UPDATE' AND OLD.status IN ('HELD', 'CONFIRMED') THEN
                        RETURN NEW;
                    END IF;

                    -- Re-entrant if the caller already holds this lock in the same transaction.
                    SELECT total_seats INTO v_total
                      FROM admission_classes WHERE id = NEW.class_id FOR UPDATE;

                    SELECT count(*) INTO v_active
                      FROM admission_holds h
                     WHERE h.class_id = NEW.class_id
                       AND h.id <> NEW.id
                       AND (h.status = 'CONFIRMED'
                            OR (h.status = 'HELD' AND h.expires_at > NEW.created_at));

                    IF v_active >= v_total THEN
                        RAISE EXCEPTION 'Class % has no remaining seats', NEW.class_id
                            USING ERRCODE = 'check_violation', CONSTRAINT = 'trg_admission_holds_capacity';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_admission_holds_capacity
                BEFORE INSERT OR UPDATE ON admission_holds
                FOR EACH ROW EXECUTE FUNCTION fn_admission_holds_enforce_capacity();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_admission_holds_capacity ON admission_holds;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_admission_holds_enforce_capacity();");

            migrationBuilder.DropTable(
                name: "admission_holds");

            migrationBuilder.DropTable(
                name: "admission_classes");
        }
    }
}
