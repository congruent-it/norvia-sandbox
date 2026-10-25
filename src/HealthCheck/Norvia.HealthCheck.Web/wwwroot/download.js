// Hands the browser a text the page built, to save as a file. Nothing is sent to the server.
window.norviaDownloadText = (fileName, text, contentType) => {
    const url = URL.createObjectURL(new Blob([text], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
};
