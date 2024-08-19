function selectFolder() {
    var picker = new Windows.Storage.Pickers.FolderPicker();
    picker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;

    picker.fileTypeFilter.replaceAll(["."]);

    return picker.pickSingleFolderAsync();
}

function downloadFile(url) {
    var client = new Windows.Web.Http.HttpClient();
    var uri = new Windows.Foundation.Uri(url);

    return client.getBufferAsync(uri).then(function (buffer) {
        return buffer;
    });
}

function saveFileToFolder(folder, fileUrl, fileName) {
    return downloadFile(fileUrl).then(function (fileData) {
        return fileData;
    }).then(function (fileData) {
        return folder.createFileAsync(fileName, Windows.Storage.CreationCollisionOption.generateUniqueName).then(function (file) {
            return Windows.Storage.FileIO.writeBufferAsync(file, fileData);
        });
    });
}

function downloadVideo(fileUrl, fileName) {
    if (selectedfolder) {
        $("#sharetext").html("Downloading video to " + selectedfolder.path + " ..")
        $("#downloadbutton").html('<img src="images/download-running.gif">');
        try {
            return saveFileToFolder(selectedfolder, fileUrl, fileName)
                .then(function () {
                    $("#sharetext").html("Downloaded to " + selectedfolder.path)
                    $("#downloadbutton").html('<img src="images/download.png">');
                    setTimeout(clearSharetext, 3000)
                });
        }
        catch (error) {
            $("#sharetext").html("An error occurred:", error)
            $("#downloadbutton").html('<img src="images/download.png">');
        }
    }
    else {
        try {
            selectFolder().then(function (folder) {
                if (folder) {
                    selectedfolder = folder;
                    console.log(folder)
                    console.log(selectedfolder)
                    $("#sharetext").html("Downloading video to " + selectedfolder.path + " ..")
                    $("#downloadbutton").html('<img src="images/download-running.gif">');
                    return saveFileToFolder(folder, fileUrl, fileName);
                }
                else {
                    $("#sharetext").html("No folder selected.")
                    $("#downloadbutton").html('<img src="images/download.png">');
                    return;
                }
            }).then(function () {
                $("#sharetext").html("Downloaded to " + selectedfolder.path)
                $("#downloadbutton").html('<img src="images/download.png">');
                setTimeout(clearSharetext, 3000)
            });
        }
        catch (error) {
            $("#sharetext").html("An error occurred:", error)
            $("#downloadbutton").html('<img src="images/download.png">');
        }
    }

}