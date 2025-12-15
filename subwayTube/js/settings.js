function showSettings() {
    tab = 'settings';
    $("#topsettings").hide();
    $(".navbutton").css("background-color", "#111");
    $("#nav_settings").css("background-color", "Highlight");

    var output = `<h2 style="text-align: left;">Settings</h2>
                  <div style="text-align: left; line-height: 150%;">
                    <div>subwayTube v` + appstring + ` by mavodev <br/> powered by InnerTube</div>
                    <div id="setting_serverlist"></div>`;

    // Server list removed as InnerTube connects directly to YouTube

    output += `<div style="width: 100%;height:30px"></div>
                    <table style="width:100%;">
                    <tr>
                      <td style="width:33%"><div class="settingsbutton" id="backupbutton" onclick='createBackup()'><img src="images/backup.png" /><br/>Backup</div></td>
                      <td><div class="settingsbutton" id="restorebutton" onclick='restoreBackup()'><img src="images/restore.png" /><br/>Restore</div></td>
                      <td style="width:33%"><div class="settingsbutton" id="clearbutton" onclick='clearHistory()'><img src="images/clean.png" /><br/>Clear history</div></td>
                    </tr>
                    </table>
               
                    <div id="settingstext"></div>
                    <div id="downloadpath">Download folder: <br/>`+ downloadFolder + ` <button onclick="selectDownloadPath()">Change</button></div>
                    <div id="streamquality"></div>
                    <div id="spacer"></div>
                  </div>`
    if (tab == 'settings') {
        $('#content').html(output);
        //getDownloads();
    }

    getStreamquality()
}

function hideSettings() {
    $("#settingsmenu").hide();
}


function applySettings() {
    let quality = $("#streamqualityselect").val()
    localStorage.streamquality = quality;
    streamquality = localStorage.streamquality;
    showServerstats()
}


function getStreamquality() {
    let qualities = ['144p', '360p', '720p', '1080p'];

    let output = `<label for="streamqualityselect">Stream Quality:</label>
                  <select name="streamqualityselect" id="streamqualityselect" onchange="applySettings()">`;
    for (let i = 0; i < qualities.length; i++) {
        let quality = qualities[i];
        let qualitytext = quality + ' (audio issues)';
        if (quality == '360p') {
            qualitytext = quality + ' (recommended)';
        }
        if (quality == streamquality) {
            output += '<option value="' + quality + '" selected>' + qualitytext + '</option>'
        }
        else {
            output += '<option value="' + quality + '">' + qualitytext + '</option>'
        }
    }
    output += '</select>';
    $("#streamquality").html(output);
}

function createBackup() {
    var now = new Date();
    var datestamp = ("0" + now.getDate()).slice(-2) + "-" + ("0" + (now.getMonth() + 1)).slice(-2) + "-" + now.getFullYear();
    var backupJSON = {};
    backupJSON.info = 'subwayTube backup';
    backupJSON.date = datestamp;
    backupJSON.subscriptions = subscriptions;
    backupJSON.playhistory = playhistory;

    var savePicker = new Windows.Storage.Pickers.FileSavePicker();
    savePicker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
    savePicker.fileTypeChoices.insert("JSON", [".json"]);
    savePicker.suggestedFileName = "subwaytube_backup";

    savePicker.pickSaveFileAsync().then(function (file) {
        if (file != null) {
            let jsonstring = JSON.stringify(backupJSON);
            Windows.Storage.FileIO.writeTextAsync(file, jsonstring).done(function () {
                Windows.Storage.CachedFileManager.completeUpdatesAsync(file).done(function (updateStatus) {
                    if (updateStatus === Windows.Storage.Provider.FileUpdateStatus.complete) {
                        console.log("File " + file.name + " was saved.");
                        $("#settingstext").html("File " + file.name + " was saved.");
                        setTimeout(clearSettingstext, 3000)
                    } else {
                        console.log("File " + file.name + " couldn't be saved.");
                        $("#settingstext").html("File " + file.name + " couldn't be saved.");
                        setTimeout(clearSettingstext, 3000)
                    }
                });
            });
        }
        else {
            console.log("Canceled");
        }

    });
}

function selectBackupfile() {
    var picker = new Windows.Storage.Pickers.FileOpenPicker();
    picker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
    picker.fileTypeFilter.append(".json");
    return picker.pickSingleFileAsync();
}

function restoreBackup() {
    selectBackupfile().then(function (file) {
        try {
            Windows.Storage.FileIO.readTextAsync(file).then(function (fileContent) {
                var importedChannels = 0;
                var importedHistory = 0;
                var importArray = JSON.parse(fileContent);

                var fileinfo = importArray.info;
                var filedate = importArray.date;

                if (fileinfo != 'microTube backup' && fileinfo != 'subwayTube backup') {
                    $("#settingstext").html('No valid file. Only subwayTube exports can be restored.');
                    setTimeout(clearSettingstext, 3000)
                    return;
                }

                var filesubs = importArray.subscriptions;

                for (let i = 0; i < filesubs.length; i++) {
                    channel = filesubs[i];
                    authorId = channel.authorId
                    author = channel.author
                    authorThumbnail = channel.image
                    followChannel(authorId, author, authorThumbnail)
                    importedChannels++
                }

                var historybackup = importArray.playhistory;

                for (let i = 0; i < historybackup.length; i++) {
                    historyitem = historybackup[i];
                    id = historyitem.id;
                    name = historyitem.name;
                    image = historyitem.image;
                    authorId = historyitem.authorId
                    author = historyitem.author

                    addHistoryItem(id, name, image, authorId, author);
                    importedHistory++
                }
                let message = 'Restored: ' + importedChannels + ' channels and ' + importedHistory + ' history from ' + filedate;
                $("#settingstext").html(message);
                setTimeout(clearSettingstext, 3000)
            });
        } catch (err) {
            console.error(err);
        }
    });
}

function clearHistory() {
    playhistory = [];
    searchhistory = [];
    localStorage.playhistory = JSON.stringify(playhistory);
    localStorage.searchhistory = JSON.stringify(searchhistory);
    console.log('History has been cleared.')
    $("#settingstext").html('History has been cleared.')
    setTimeout(clearSettingstext, 3000)
}

function clearSettingstext() {
    $("#settingstext").html('')
}