const connection = new signalR.HubConnectionBuilder()
    .withUrl("/taskHub")
    .build();

connection.on("ReceiveTaskUpdate", (msg) => {
    console.log("Realtime event:", msg);
    reloadTasks();
});

connection.start().then(() => console.log("Connected to TaskHub ✅"));

async function reloadTasks() {
    const res = await fetch("/Tasks/IndexV2");
    const html = await res.text();
    const parser = new DOMParser();
    const doc = parser.parseFromString(html, "text/html");
    const newList = doc.querySelector("#taskList");
    document.querySelector("#taskList").innerHTML = newList.innerHTML;
}

// Create Task
document.querySelector("#createTaskForm")?.addEventListener("submit", async (e) => {
    e.preventDefault();
    const form = e.target;
    const data = Object.fromEntries(new FormData(form).entries());
    const res = await fetch("/Tasks/CreateV2", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(data)
    });
    if (res.ok) {
        form.reset();
        bootstrap.Modal.getInstance(document.querySelector("#createModal")).hide();
        reloadTasks();
    }
});

// Update Status
document.addEventListener("click", async (e) => {
    if (e.target.classList.contains("update-status")) {
        const id = e.target.dataset.id;
        const status = e.target.dataset.status;
        await fetch("/Tasks/UpdateStatusV2", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ id, status })
        });
    }

    if (e.target.classList.contains("delete-task")) {
        const id = e.target.dataset.id;
        if (confirm("Xóa task này?")) {
            await fetch(`/Tasks/DeleteV2?id=${id}`, { method: "POST" });
        }
    }
});
