import { test, expect } from "@playwright/test";
for (const framework of ["react", "vue", "svelte", "angular", "astro"]) {
  test(`${framework} renders custom rows and uploads through the real server`, async ({
    page,
  }) => {
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await page.goto(`/frameworks/${framework}/`);
    await page
      .getByLabel("Select files")
      .setInputFiles({
        name: `${framework}-document.txt`,
        mimeType: "text/plain",
        buffer: Buffer.alloc(600000, 65),
      });
    const row = page.locator("article.custom-row");
    await expect(row).toContainText(`${framework}-document.txt`);
    if (framework !== "astro") {
      await expect(
        page.getByRole("button", { name: "Disabled upload", exact: true }),
      ).toBeDisabled();
      await page
        .getByRole("button", { name: "Blocked upload", exact: true })
        .click();
      await expect(row).toHaveAttribute("data-state", "idle");
    }
    await page.getByRole("button", { name: "Upload", exact: true }).click();
    await expect(row).toHaveAttribute("data-state", "completed");
    await expect(row.getByRole("progressbar")).toHaveAttribute(
      "aria-valuenow",
      "100",
    );
    if (framework !== "astro") {
      await page.getByRole("button", { name: "Disable root" }).click();
      await expect(page.getByLabel("Select files")).toBeDisabled();
    } else
      await expect(page.locator("[data-upload-output]")).toContainText(
        "completed",
      );
    expect(errors).toEqual([]);
  });
}
test("HTTP and indeterminate recipes use the local single-request endpoint", async ({
  page,
}) => {
  await page.goto("/");
  for (const kind of ["http", "indeterminate"]) {
    const root = page.locator(`#${kind}`);
    await root
      .locator("[data-upload-input]")
      .setInputFiles({
        name: `${kind}.txt`,
        mimeType: "text/plain",
        buffer: Buffer.from("single request body"),
      });
    await root
      .getByRole("button", { name: "Upload files", exact: true })
      .click();
    await expect(root.locator(".file-status")).toHaveText("completed");
    await expect(root.locator("[data-percentage]")).toHaveText("100%");
  }
});
