namespace BlazorStoc.Services;

// SQL shared by the exit repository and the component repository: which active components of a project have a product in the latest revision of their offer
// (a line in pieces tied to that product).
internal static class ComponentExitSql
{
    public const string ComponentsWithProduct = """
        SELECT DISTINCT pc.id FROM project_components pc
        INNER JOIN offers o ON o.project_id=pc.project_id AND o.system_type_id=pc.system_type_id
        INNER JOIN offer_lines l ON l.offer_id=o.id AND l.product_id=@product AND l.in_stock=1
        WHERE pc.project_id=@project AND pc.archived_utc IS NULL
          AND o.revision=(SELECT MAX(o2.revision) FROM offers o2 WHERE o2.number_key=o.number_key)
        """;
}
