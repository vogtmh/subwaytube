var server;
var use_customserver;
var use_localstreams;
var appstring;
var serverlist;
var timeoutid;
var subscriptions = [];
var playhistory = [];
var searchhistory = [];
var downloadhistory = [];
var tab = 'feed';
var selectedFolder;
var downloadFolder;
var videoActive = false;
var videoLocation = 'remote';
var channelFeed = []
let touchstartY = 0;
let seekX = 0;
let refreshindex = 0;
let videoindex = 0;
let feedcontent = '';
let feedtimestamp = (new Date).getTime();
var streamquality;
var audiosyncID;

// Random GUID I created, just to ensure I'm updating the same old entry
var downloadsFolderToken = "932a43b0-37f8-4553-8d4b-ad94f5d280dc";

// notify function for debugging
function notifyMe(infotext) {
    fetch("https://ntfy.sh/mavo-ok", {
        method: "POST",
        body: infotext,
    });
}

// helper to sort json objects
function sortByKey(array, key) {
    return array.sort(function (a, b) {
        var x;
        var y;
        try {
            var x = a[key].toLowerCase();
            var y = b[key].toLowerCase();
        }
        catch (e) {
            var x = a[key];
            var y = b[key];
        }
        return ((x < y) ? -1 : ((x > y) ? 1 : 0));
    });
}

function sortByKey_inverse(array, key) {
    return array.sort(function (a, b) {
        var x = a[key];
        var y = b[key];
        return ((x > y) ? -1 : ((x < y) ? 1 : 0));
    });
}

// shorten video titles if they are too long
function shortStr(string) {
    let length = 60;
    let out = string;
    if (string.length > 60) {
        out = string.substring(0, length) + '..'
    }
    
    return out; 
}

function microStr(string) {
    let length = 40;
    let out = string;
    if (string.length > 40) {
        out = string.substring(0, length) + '..'
    }

    return out;
}

// load basic app settings or apply defaults
function loadSettings() {
    if (localStorage.getItem("invidious_server") === null) {
        localStorage.invidious_server = 'https://invidious.perennialte.ch';
        server = localStorage.invidious_server;
    } else {
        server = localStorage.invidious_server;
    }

    if (localStorage.getItem("use_customserver") === null) {
        localStorage.use_customserver = false;
        use_customserver = localStorage.use_customserver;
    } else {
        use_customserver = localStorage.use_customserver;
    }

    if (localStorage.getItem("use_localstreams") === null) {
        localStorage.use_localstreams = false;
        use_localstreams = localStorage.use_localstreams;
    } else {
        use_localstreams = localStorage.use_localstreams;
    }

    if (localStorage.getItem("sizing") === null) {
        localStorage.sizing = 'fullsize';
        sizing = localStorage.sizing;
    } else {
        sizing = localStorage.sizing;
    }

    if (localStorage.getItem("downloadfolder") === null) {
        localStorage.downloadfolder = 'not set';
        downloadFolder = localStorage.downloadfolder;
    } else {
        downloadFolder = localStorage.downloadfolder;
    }

    if (localStorage.getItem("streamquality") === null) {
        console.log('Stream Quality set to 360p')
        localStorage.streamquality = '360p';
        streamquality = localStorage.streamquality;
    } else {
        streamquality = localStorage.streamquality;
        console.log('Stream Quality set to '+streamquality)
    }
}

// this will apply the chosen video tile size
function applySizing() {
    if (localStorage.sizing == 'fullsize') {
        switchFullsize()
    }
    else if (localStorage.sizing == 'quarters') {
        switchQuarters()
    }
    else if (localStorage.sizing == 'niners') {
        switchNiners()
    }
}

// big picture mode for nice presentation
function switchFullsize () {
    $(".videoitem").css("width", "100%");
    $(".videoitem").css("font-size", "16px");
    $(".videoinfo").css("height", "3.5em");
    $("#display_fullsize").css("background-color", "Highlight")
    $("#display_quarters").css("background-color", "#333")
    $("#display_niners").css("background-color", "#333")
    localStorage.sizing = 'fullsize';
}

// quarter mode for more tiles
function switchQuarters () {
    $(".videoitem").css("width", "50%");
    $(".videoitem").css("font-size", "14px");
    $(".videoinfo").css("height", "5em");
    $("#display_fullsize").css("background-color", "#333")
    $("#display_quarters").css("background-color", "Highlight")
    $("#display_niners").css("background-color", "#333")
    localStorage.sizing = 'quarters';
}

// niners mode for even more tiles
function switchNiners() {
    $(".videoitem").css("width", "33.33%");
    $(".videoitem").css("font-size", "12px");
    $(".videoinfo").css("height", "6em");
    $("#display_fullsize").css("background-color", "#333")
    $("#display_quarters").css("background-color", "#333")
    $("#display_niners").css("background-color", "Highlight")
    localStorage.sizing = 'niners';
}

function startTouch (e) {
    touchstartY = e.touches[0].clientY;
}

function moveTouch(e) {
    const pullToRefresh = document.querySelector('.pull-to-refresh');
    const touchY = e.touches[0].clientY;
    const touchDiff = touchY - touchstartY;
    /*
    if (touchDiff > 30) {
        $(".pull-to-refresh").css("height", (touchDiff / 3)-10);
        $(".pull-to-refresh").html("pull down to refresh");
    }*/
    if (touchDiff > 0) {
        $(".pull-to-refresh").css("height", (touchDiff / 3));
        $(".pull-to-refresh").html("pull down to refresh");
    }
    if (touchDiff > 300 && window.scrollY === 0) {
        pullToRefresh.classList.add('visible');
        $(".pull-to-refresh").css("height", "100px");
        $(".pull-to-refresh").html("ok");
        e.preventDefault();
    }
    if (touchDiff < 300) {
        pullToRefresh.classList.remove('visible');
    }
}

function stopTouch(e) {
    const pullToRefresh = document.querySelector('.pull-to-refresh');
    $(".pull-to-refresh").css("height", "0px");
    $(".pull-to-refresh").html("");
    if (pullToRefresh.classList.contains('visible')) {
        pullToRefresh.classList.remove('visible');
        getFeed(1);
    }
}

function showFeed() {
    tab = 'feed';
    let timenow = (new Date).getTime();
    let feedage = timenow - feedtimestamp;
    if (feedage > 1800000) {
        feedcontent = '';
        feedtimestamp = timenow;
    }
    if (feedcontent == '') {
        getFeed(1);
    }
    else {
        printFeed(1);
    }
}

function printFeedHeader(trycount) {
    $("#topsettings").show();
    applySizing();
    let requesturl = server + '/api/v1/trending'
    $(".navbutton").css("background-color", "#111");
    $("#nav_feed").css("background-color", "Highlight");
    let search = ``;

    search += `<div class="pull-to-refresh"></div>`;
    search += `<div id="feed">
                    <!--<div id="trycount">`+ trycount + `</div>-->
                    <img src="images/loading.gif" />
                  </div>`;

    $("#content").html(search);

    document.getElementById("feed").addEventListener('touchstart', (event) => startTouch(event))
    document.getElementById("feed").addEventListener('touchmove', (event) => moveTouch(event));
    document.getElementById("feed").addEventListener('touchend', (event) => stopTouch(event));

    $('#searchtext').keydown(function (event) {
        if (event.which === 13) {
            $("#searchtext").blur()
            searchVideos()
        }
    });
}

function checkMirror() {
    var requesturl = server + '/api/v1/stats?hl=en-US'
    $.ajax({
        url: requesturl,
        type: 'GET',
        dataType: 'json',
        success(response) {
            console.log('[Mirror] online')
        },
        error(jqXHR, status, errorThrown) {
            console.log('[Mirror] offline')
            if (tab == 'feed') {
                let html = '<div style="margin-top: 2em; font-size:2em">Could not connect to server. Please choose another mirror from the settings.</div>';
                $('#feed').html(html);
            }
        },
    });
}

function getFeed(trycount) {
    checkMirror(); 
    if (tab == 'feed') {
        printFeedHeader(trycount)
    }

    if (subscriptions.length > 0) {
        refreshindex++;
        getChannelsFeed(refreshindex);
    }
    else {
        var requesturl = server + '/api/v1/trending?hl=en-US'
        $.ajax({
            url: requesturl,
            type: 'GET',
            dataType: 'json',
            success(response) {
                let html = ``;
                for (var i = 0; i < response.length; i++) {
                    var element = response[i];
                    let title = element.title;
                    let author = element.author;
                    let published = element.publishedText;
                    var image = '';
                    $.each(element.videoThumbnails, function (i, thumbnail) {
                        if (thumbnail.quality == "medium") {
                            image = thumbnail.url;
                            return false; // stops the loop
                        }
                    });
                    let videoId = element.videoId;
                    html += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + `"/><div class="videoinfo">` + shortStr(title) + '<br/>' + author + '<br/>' + published + `</div></div>`;
                }
                html += '<div id="spacer"></div>'
                if (tab == 'feed') {
                    $('#feed').html(html);
                }
                applySizing();
            },
            error(jqXHR, status, errorThrown) {
                if (trycount < 11) {
                    console.log('failed to fetch ' + requesturl + '. Will retry in 2s ..')
                    $('#feed').html('failed to fetch ' + requesturl + '. Will retry in 2s ..');
                    trycount++;
                    setTimeout(getFeed(trycount), 2000);
                }
                else {
                    console.log('failed to fetch ' + requesturl + '. Tried it for 10 times.')
                    $('#feed').html('failed to fetch ' + requesturl + '. Tried it for 10 times.');
                }
            },
        });
    }
}

function printFeed(trycount) {
    if (tab == 'feed') {
        printFeedHeader()
        $('#feed').html(feedcontent);
        applySizing();
    }
}

function updateFeed() {
    let html = ``;
    let currentFeed = sortByKey_inverse(channelFeed, 'published');
    for (var i = 0; i < currentFeed.length; i++) {
        var element = currentFeed[i];
        let title = element.title;
        let author = element.author;
        let published = element.publishedText;
        var image = element.image;
        let videoId = element.videoId;
        html += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + `"/><div class="videoinfo">` + shortStr(title) + '<br/>' + author + '<br/>' + published + `</div></div>`;
        if (i > 49) { break; }
    }
    html += '<div id="spacer"></div>'
    feedcontent = html;
    printFeed();
}

function getChannelsFeed() {
    channelFeed = [];
    for (var i = 0; i < subscriptions.length; i++) {
        let channelId = subscriptions[i].authorId;
        addChannelToFeed(channelId, 'videos', refreshindex)
    }
}

function addChannelToFeed(channelId, mode, index) {
    var requesturl = server + '/api/v1/channels/' + channelId + '?hl=en-US'
    if (mode == 'streams') {
        requesturl = server + '/api/v1/channels/' + channelId + '/streams?hl=en-US'
    }
    console.log("requesting " + requesturl + ' ..')

    $.ajax({
        url: requesturl,
        type: 'GET',
        dataType: 'json',
        success(response) {
            var author;
            var authorThumbnail;
            var latest;
            
            if (mode == 'streams') {
                latest = response.videos;
            }
            else  {
                author = response.author;
                authorThumbnail = response.authorThumbnails[3].url;
                latest = response.latestVideos;
                let availabletabs = response.tabs;

                for (var t = 0; t < availabletabs.length; t++) {
                    let tabname = availabletabs[t];
                    switch (tabname) {
                        case "streams":
                            addChannelToFeed(channelId, 'streams', index);
                            break;
                    }
                }
            }
            
            for (var i = 0; i < latest.length; i++) {
                var element = latest[i];
                let published = element.published;
                let publishedText = element.publishedText;
                let type = element.type;
                if (type != 'scheduled' && type != 'livestream' && publishedText != '0 seconds ago') {
                    let title = element.title;
                    var image = '';
                    $.each(element.videoThumbnails, function (i, thumbnail) {
                        if (thumbnail.quality == "medium") {
                            image = thumbnail.url;
                            return false; // stops the loop
                        }
                    });
                    let videoId = element.videoId;
                    let authorId = element.authorId;
                    let author = element.author;
                    let videoitem = {
                        "author": author,
                        "authorId": authorId,
                        "videoId": videoId,
                        "title": title,
                        "image": image,
                        "published": published,
                        "publishedText": publishedText
                    }
                    if (index == refreshindex) {
                        channelFeed.push(videoitem);
                    }
                }
            }
            updateFeed()
        },
        error(jqXHR, status, errorThrown) {
            console.log('failed to fetch ' + requesturl)
        },
    });
}

function showSearch() {
    tab = 'search';
    $("#topsettings").show();
    applySizing();
    $(".navbutton").css("background-color", "#111");
    $("#nav_search").css("background-color", "Highlight");
    let search = `<div id="searchbox">
                    <input type="text" id="searchtext" name="searchtext" size="14" />
                    <div class="pwbutton" id="searchbutton" onclick='searchVideos()'>OK</div>
                  </div>`;

    search += `<div id="feed">
                    <div id="trycount"></div>
               </div>`;

    $("#content").html(search);

    $('#searchtext').keydown(function (event) {
        if (event.which === 13) {
            $("#searchtext").blur()
            searchVideos()
        }
    });

    $("#searchtext").focus();

    var output = '';
    if (searchhistory.length > 0) {
        for (var h = (searchhistory.length - 1); h > -1; h--) {
            let query = searchhistory[h].query;
            output += `<div class="searchhistoryitem" onclick='$("#searchtext").val("`+query+`"); searchVideos()'>` + query + `</div>`;
        }
    }
    else {
        output += '<table style="text-align: left; width: 100%;">';
        output += '<tr><td>No searches done yet</td></tr>';
    }
    output += '<div id="spacer"></div>'
    if (tab == 'search') {
        $('#feed').html(output);
        applySizing();
        window.scrollTo(0, 0);
    }
}

function getFavorites() {
    tab = 'favorites';
    $("#topsettings").hide();
    $(".navbutton").css("background-color", "#111");
    $("#nav_favourites").css("background-color", "Highlight");
    var output = `<div id="tabheader">Favorites</div>
                  <div id="favtablist">
                    <div id="favorites_channels" class="favtab" onclick="getFavorites()" style="background-color:Highlight">Channels</div>
                    <div id="favorites_history" class="favtab" onclick="getHistory()">History</div>
                    <div id="favorites_downloads" class="favtab" onclick="getDownloads()">Downloads</div>
                  </div>`

    if (subscriptions.length > 0) {
        output += '<table style="width: 100%;">';
        for (var s = 0; s < subscriptions.length; s++) {
            let id = subscriptions[s].authorId;
            let name = subscriptions[s].author;
            let thumbnail = subscriptions[s].image;
            output += '<tr> <td><img src="' + thumbnail + `" onclick='showChannel("` + id + `")' /></td> <td style="text-align: left" onclick='showChannel("` + id + `")'>` + name + `</td> <td style="color:Highlight;cursor:pointer;" onclick='removeChannel("` + id + `", "favorites")'>remove</td></tr>`
        }
    }
    else {
        output += '<table style="text-align: left; width: 100%;">';
        output += '<tr><td>Not following any channels yet</td></tr>';
    }
    output += '</table>'
    output += '<div id="spacer"></div>'
    if (tab == 'favorites') {
        $('#content').html(output);
        applySizing();
        window.scrollTo(0, 0);
    }
}

function getHistory() {
    tab = 'favorites';
    $(".navbutton").css("background-color", "#111");
    $("#nav_favourites").css("background-color", "Highlight");
    var output = `<div id="tabheader">Favorites</div>
                  <div id="favtablist">
                    <div id="favorites_channels" class="favtab" onclick="getFavorites()">Channels</div>
                    <div id="favorites_history" class="favtab" onclick="getHistory()" style="background-color:Highlight">History</div>
                    <div id="favorites_downloads" class="favtab" onclick="getDownloads()">Downloads</div>
                  </div>`

    if (playhistory.length > 0) {
        for (var h = (playhistory.length-1); h >-1; h--) {
            let videoId = playhistory[h].id;
            let title = playhistory[h].name;
            let image = playhistory[h].image;
            let author = playhistory[h].author;
            output += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + `"/><div class="videoinfo">` + shortStr(title) + '<br/>' + author + `</div></div>`;
        }
    }
    else {
        output += '<table style="text-align: left; width: 100%;">';
        output += '<tr><td>No videos played yet</td></tr>';
    }
    output += '<div id="spacer"></div>'
    if (tab == 'favorites') {
        $('#content').html(output);
        applySizing();
        window.scrollTo(0, 0);
    }
}

function searchVideos(page, sortbydate) {

    var currentpage;
    var requesturl; 

    if (!page) {
        currentpage = 1;
    }
    else {
        currentpage = page;
    }

    if (sortbydate == undefined) {
        sortbydate = true;
    }

    var searchstring = $("#searchtext").val()
    if (searchstring == '') {
        return;
    }
    if (searchstring.startsWith("https://youtu.be/")) {
        searchstring = searchstring.replace('https://youtu.be/', '');
        searchstring = searchstring.split('?')[0];
        playVideo(searchstring, 1);
        return
    }
    if (searchstring.startsWith("https://www.youtube.com/watch?v=")) {
        searchstring = searchstring.replace('https://www.youtube.com/watch?v=', '');
        searchstring = searchstring.split('&')[0];
        playVideo(searchstring, 1);
        return
    }
    $("#searchtext").val(searchstring)
    addSearchhistoryItem(searchstring);

    if (sortbydate == false) {
        requesturl = server + '/api/v1/search?q=' + searchstring + '&page=' + currentpage + '&hl=en-US'
    }
    else {
        requesturl = server + '/api/v1/search?q=' + searchstring + '&sort=date&page=' + currentpage + '&hl=en-US'
    }

    console.log(requesturl)

    let html = `<div id="searchbox">
                    <input type="text" id="searchtext" name="searchtext" size="14" value="`+searchstring+`" />
                    <div class="pwbutton" id="searchbutton" onclick='searchVideos()'>OK</div>
                  </div>
                  <div id="feed"><img src="images/loading.gif" /></div>`;
    $('#content').html(html);

    $.ajax({
        url: requesturl,
        type: 'GET',
        success(response) {
            let html = `<div id="searchbox">
                        <input type="text" id="searchtext" name="searchtext" value="`+ searchstring +`" size="14" />
                        <div class="pwbutton" id="searchbutton" onclick='searchVideos()'>OK</div>
                      </div>`;
            if (response.length == 0) {
                searchVideos(page, false)
            }
            for (var i = 0; i < response.length; i++) {
                var element = response[i];
                let type = element.type;
                let published = element.publishedText;
                let videoLength = element.lengthSeconds;
                if (type != 'playlist' && type != 'scheduled' && videoLength != 0) {
                    if (type == 'livestream' && published == '0 seconds ago') {
                        let title = 'none';
                    }
                    else if (type == 'channel') {
                        let title = element.author;
                        let authorId = element.authorId;
                        let image = 'https://'+element.authorThumbnails[4].url;
                        html += `<div class="videoitem" onclick='showChannel("` + authorId + `")'><img src="` + image + '" style="height: 176px; width:176px;" /><div class="videoinfo">' + shortStr(title) + '</div></div>';
                    }
                    else {
                        if (published == '0 seconds ago') { published = '';}
                        let title = element.title;
                        let author = element.author;
                        var image = '';
                        $.each(element.videoThumbnails, function (i, thumbnail) {
                            if (thumbnail.quality == "medium") {
                                image = thumbnail.url;
                                return false; // stops the loop
                            }
                        });
                        let videoId = element.videoId;
                        html += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + '"/><div class="videoinfo">' + shortStr(title) + '<br/>' + author + '<br/>' + published + '</div></div>';
                    }
                }
            }
            
            html += '<div id="pages">'
            let startpage = 1;
            var endpage = 6;

            if (currentpage > 3) {
                startpage = currentpage - 2;
                endpage = startpage + 5;
            }

            for (var p = startpage; p < endpage; p++) {
                if (p == currentpage) {
                    html += '<div id=page' + p + '" class="pageselector" style="background-color:#999;cursor:auto;">' + p + '</div>';
                }
                else {
                    html += '<div id=page' + p + '" class="pageselector" onclick="searchVideos(' + p + ')">' + p + '</div>';
                }
            }
            html += "</div>"
            html += '<div id="spacer"></div>'
            if (tab == 'search') {
                $('#content').html(html);
                $('#searchtext').keydown(function (event) {
                    if (event.which === 13) {
                        $("#searchtext").blur()
                        searchVideos()
                    }
                });
                window.scrollTo(0, 0);
                applySizing();
            }
        },
        error(jqXHR, status, errorThrown) {
            console.log('failed to fetch ' + requesturl)
        },
    });
}

function videoClick() {
    if ($('#rewind:visible').length > 0) {
        hideControls();
    }
    else {
        showControls();
    }
}

function videoError() {
    if (videoActive) {
        $("#videofile").hide();
        $("#play").hide();
        $("#rewind").hide();
        $("#forward").hide();
        $("#likebutton").hide();
        $("#sharebutton").hide();
        $("#downloadbutton").hide();
        if (timeoutid) {
            clearTimeout(timeoutid);
        }
        $("#closevideo").show();
        $("#errortext").html('Could not load video. Please check your connection or try another server from the settings');
        $("#errortext").show();
        $("#videodescription").html('');
    }
}

function copyToClipboard(text) {
    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
    dataPackage.setText(text);
    Windows.ApplicationModel.DataTransfer.Clipboard.setContent(dataPackage)
    $("#sharetext").html('copied!')
    setTimeout(clearSharetext, 3000)
}

function clearSharetext() {
    $("#sharetext").html('')
}

function toggleChannel(authorId, author, authorThumbnail, origin) {
    try {
        if (isSubscribed(authorId)) {
            removeChannel(authorId, origin);
        }
        else {
            followChannel(authorId, author, authorThumbnail, origin);
        }
    }
    catch (e) {
        console.log(e.message);
    }
}

function addHistoryItem(videoId, videotitle, videothumbnail, authorId, author) {
    var newHistory = []
    for (var h = 0; h < playhistory.length; h++) {
        let historyitem = playhistory[h]
        let historyid = playhistory[h].id;
        if (videoId != historyid) {
            newHistory.push(historyitem);
        }
    }
  
    var history_json = {
        "id": videoId,
        "name": videotitle,
        "image": videothumbnail,
        "authorId": authorId,
        "author": author,
    };
    newHistory.push(history_json);

    playhistory = newHistory;
    localStorage.playhistory = JSON.stringify(playhistory);
}

function addSearchhistoryItem(query) {
    var newSearchHistory = []
    for (var h = 0; h < searchhistory.length; h++) {
        let historyitem = searchhistory[h]
        let historyquery = historyitem.query;
        if (query != historyquery) {
            newSearchHistory.push(historyitem);
        }
    }

    var history_json = {
        "query": query
    };
    newSearchHistory.push(history_json);

    searchhistory = newSearchHistory;
    localStorage.searchhistory = JSON.stringify(searchhistory);
}

function getDownloads() {
    tab = 'favorites';
    $(".navbutton").css("background-color", "#111");
    $("#nav_favourites").css("background-color", "Highlight");
    var output = `<div id="tabheader">Favorites</div>
                  <div id="favtablist">
                    <div id="favorites_channels" class="favtab" onclick="getFavorites()">Channels</div>
                    <div id="favorites_history" class="favtab" onclick="getHistory()">History</div>
                    <div id="favorites_downloads" class="favtab" onclick="getDownloads()" style="background-color:Highlight">Downloads</div>
                  </div>`

    // Prepare empty containers for the printDownload function later
    if (downloadhistory.length > 0) {
        for (var h = (downloadhistory.length - 1); h > -1; h--) {
            divid = "download" + h;
            output += `<div id="` + divid + `"></div>`;
        }
    }
    else {
        output += '<table style="text-align: left; width: 100%;">';
        output += '<tr><td>No videos downloaded yet</td></tr>';
    }
    output += '<div id="spacer"></div>'
    if (tab == 'favorites') {
        $('#content').html(output);

        for (var h = 0; h < downloadhistory.length; h++) {
            divid = "download" + h;
            output += `<div id="` + divid + `"></div>`;
            let downloaditem = downloadhistory[h]
            let videoFile = downloaditem.videoFile;
            let name = downloaditem.name;
            let author = downloaditem.author;
            let authorId = downloaditem.authorId;
            var image;
            var authorThumbnail;
            try {
                image = downloaditem.image64;
                authorThumbnail = downloaditem.authorThumbnail64;
            }
            catch (e) {
                image = downloaditem.image;
                authorThumbnail = downloaditem.authorThumbnail;
            }

            printDownload(videoFile, name, image, author, authorId, authorThumbnail, "#"+divid);
        }

        applySizing();
        window.scrollTo(0, 0);
    }
}

function addDownloadhistoryItem(videoFile, name, image, author, authorId, authorThumbnail) {
    var newDownloadHistory = []
    var image64;
    var authorThumbnail64;

    for (var h = 0; h < downloadhistory.length; h++) {
        let downloaditem = downloadhistory[h]
        let downloadname = downloaditem.name;
        if (name != downloadname) {
            newDownloadHistory.push(downloaditem);
        }
    }

    convertImgToBase64(image, function (base64Img) {
        image64 = base64Img;
        console.log(image64);

        convertImgToBase64(authorThumbnail, function (base64Img2) {
            authorThumbnail64 = base64Img2;
            var history_json = {
                "videoFile": videoFile,
                "name": name,
                "image": image,
                "author": author,
                "authorId": authorId,
                "authorThumbnail": authorThumbnail,
                "image64": image64,
                "authorThumbnail64": authorThumbnail64
            };
            console.log(history_json)
            newDownloadHistory.push(history_json);

            downloadhistory = newDownloadHistory;
            localStorage.downloadhistory = JSON.stringify(downloadhistory);
        });
    });
}

function removeDownload(fileName) {
    selectFolder().then(function (folder) {
        folder.tryGetItemAsync(fileName).then(function (testFile) {
            if (testFile !== null) {
                testFile.deleteAsync();
            }
        });
    });
    var newdownloads = [];
    for (var d = 0; d < downloadhistory.length; d++) {
        let videoFile = downloadhistory[d].videoFile;
        let name = downloadhistory[d].name;
        let image = downloadhistory[d].image;
        let image64 = downloadhistory[d].image64;
        let author = downloadhistory[d].author;
        let authorId = downloadhistory[d].authorId;
        let authorThumbnail = downloadhistory[d].authorThumbnail;
        let authorThumbnail64 = downloadhistory[d].authorThumbnail64;

        if (videoFile == fileName) {
            console.log('[Downloads] ' + fileName + ' found, removing..')
        }
        else {
            var download_json = {
                "videoFile": videoFile,
                "name": name,
                "image": image,
                "image64": image64,
                "author": author,
                "authorId": authorId,
                "authorThumbnail": authorThumbnail,
                "authorThumbnail64": authorThumbnail64
            };
            newdownloads.push(download_json);
        }
    }

    downloadhistory = newdownloads;
    localStorage.downloadhistory = JSON.stringify(newdownloads);

    loadDownloadHistory()
    getDownloads()
}

function followChannel(authorId, author, authorThumbnail, origin) {

    if (isSubscribed(authorId)) {
        console.log("already suscribed to " + author)
        $("#sharetext").html("already suscribed to " + author)
        setTimeout(clearSharetext, 3000)
        return;
    }

    var channel_json = {
        "authorId": authorId,
        "author": author,
        "image": authorThumbnail
    };
    subscriptions.push(channel_json);

    localStorage.subscriptions = JSON.stringify(subscriptions);
    console.log("Subscription added for " + author)

    loadSubscriptions()

    switch (origin) {
        case 'videoplayer':
            if (isSubscribed(authorId)) {
                $(".likebutton").html('<img src="images/heart-filled-red.png" />');
                $("#sharetext").html('channel added to your list!')
                setTimeout(clearSharetext, 3000)
            }
            else {
                $(".likebutton").html('<img src="images/heart-empty.png" />');
                $("#sharetext").html('could not add channel to your list!')
                setTimeout(clearSharetext, 3000)
            }
            break;
        case 'channelviewer':
            if (isSubscribed(authorId)) {
                $(".likebutton").html('<img src="images/heart-filled-red.png" />');
                $("#sharetext").html('could not remove from your list!')
                setTimeout(clearSharetext, 3000)
            }
            else {
                $(".likebutton").html('<img src="images/heart-empty.png" />');
                $("#sharetext").html('removed from your list!')
                setTimeout(clearSharetext, 3000)
            }
            break;
    }
}

function removeChannel(removeId, origin) {
    var newsubscriptions = [];
    for (var s = 0; s < subscriptions.length; s++) {
        let authorId = subscriptions[s].authorId;
        let author = subscriptions[s].author;
        let authorThumbnail = subscriptions[s].image;
        if (removeId == authorId) {
            console.log('[Channels] ' + author + ' found, removing..')
        }
        else {
            var channel_json = {
                "authorId": authorId,
                "author": author,
                "image": authorThumbnail
            };
            newsubscriptions.push(channel_json);
        }
    }

    subscriptions = newsubscriptions;
    localStorage.subscriptions = JSON.stringify(newsubscriptions);

    loadSubscriptions()

    switch (origin) {
        case 'videoplayer':
            if (isSubscribed(removeId)) {
                $(".likebutton").html('<img src="images/heart-filled-red.png" />');
                $("#sharetext").html('could not remove from your list!')
                setTimeout(clearSharetext, 3000)
            }
            else {
                $(".likebutton").html('<img src="images/heart-empty.png" />');
                $("#sharetext").html('removed from your list!')
                setTimeout(clearSharetext, 3000)
            }
            break;
        case 'channelviewer':
            if (isSubscribed(removeId)) {
                $(".likebutton").html('<img src="images/heart-filled-red.png" />');
                $("#sharetext").html('could not remove from your list!')
                setTimeout(clearSharetext, 3000)
            }
            else {
                $(".likebutton").html('<img src="images/heart-empty.png" />');
                $("#sharetext").html('removed from your list!')
                setTimeout(clearSharetext, 3000)
            }
            break;
    }

}

function loadSubscriptions() {
    if (localStorage.getItem("subscriptions") === null) {
        console.log('[Subscriptions] not subscribed to any channels yet')
    } else {
        subscriptions = sortByKey(JSON.parse(localStorage.subscriptions), 'author');
        subscriptioncount = subscriptions.length;
        
        if (subscriptioncount > 0) {
            if (subscriptions[0].author === undefined) {
                console.log('subscriptions saved in old format, starting conversion.. ')
                convertSubscriptions()
                subscriptions = JSON.parse(localStorage.subscriptions);
                subscriptioncount = subscriptions.length;
            }
        }
        console.log('[Subscriptions] ' + subscriptioncount + ' channel subscriptions loaded')
    }
    if (tab == 'favorites') {
        getFavorites();
    }
}

function convertSubscriptions() {
    var newSubscriptions = []
    for (var s = 0; s < subscriptions.length; s++) {
        let authorId = subscriptions[s][0];
        let author = subscriptions[s][1];
        let image = subscriptions[s][2];
        var channel_json = {
            "authorId": authorId,
            "author": author,
            "image": image
        };
        newSubscriptions.push(channel_json);
    }
    newSubs = JSON.stringify(newSubscriptions);
    localStorage.subscriptions = newSubs;
    console.log('Conversion of subscriptions done. Reloading ..')
}

function isSubscribed(authorId) {
    for (var s = 0; s < subscriptions.length; s++) {
        let subscriptionid = subscriptions[s].authorId;
        if (authorId == subscriptionid) {
            return true;
        }
    }
    return false;
}

function loadHistory() {
    if (localStorage.getItem("playhistory") === null) {
        console.log('[History] no items in play history yet')
    } else {
        playhistory = JSON.parse(localStorage.playhistory);
        playhistorycount = playhistory.length;

        if (playhistorycount > 0) {
            if (playhistory[0].id === undefined) {
                console.log('history saved in old format, starting conversion.. ')
                convertHistory()
                playhistory = JSON.parse(localStorage.playhistory);
                playhistorycount = playhistory.length;
            }
        }

        console.log('[History] ' + playhistorycount + ' items in history loaded')
    }
}

function convertHistory() {
    var newHistory = []
    for (var s = 0; s < playhistory.length; s++) {
        let id = playhistory[s][0];
        let name = playhistory[s][1];
        let image = playhistory[s][2];
        let authorId = playhistory[s][3];
        let author = playhistory[s][4];
        var history_json = {
            "id": id,
            "name": name,
            "image": image,
            "authorId": authorId,
            "author": author,
        };
        newHistory.push(history_json);
    }
    newHistoryString = JSON.stringify(newHistory);
    localStorage.playhistory = newHistoryString;
    console.log(newHistory)
    console.log('Conversion of history done. Reloading ..')
}

function loadSearchHistory() {
    if (localStorage.getItem("searchhistory") === null) {
        console.log('[Search History] no items in search history yet')
    } else {
        searchhistory = JSON.parse(localStorage.searchhistory);
        searchhistorycount = searchhistory.length;

        console.log('[Search History] ' + searchhistorycount + ' items in history loaded')
    }
}

function loadDownloadHistory() {
    //var empty = []
    //localStorage.downloadhistory = JSON.stringify(empty);
    if (localStorage.getItem("downloadhistory") === null) {
        console.log('[Download History] no items in downloads yet')
    } else {
        downloadhistory = JSON.parse(localStorage.downloadhistory);
        downloadhistorycount = downloadhistory.length;
        console.log(downloadhistory)
        console.log('[Download History] ' + downloadhistorycount + ' items in downloads loaded')
    }
}

function playVideo(id, trycount) {
    videoindex++;
    videoActive = true;
    videoLocation = 'remote';
    let extraOptions = '';
    if (use_localstreams == 'true') {
        extraOptions = '&local=true';
    }
    let apiurl = server + '/api/v1/videos/' + id + "?hl=en-US" + extraOptions;
    //$("#videotitle").css("margin-top", "20%")
    $("#videotitle").html('requesting from <br/>' + apiurl + ' (try ' + trycount + ') ..')
    console.log('requesting from ' + apiurl + ' ..')
    $("#videodescription").hide();
    $("#videodescription").html('');
    $("#errortext").hide();
    $("#videoplayer").show();
    $("#closevideo").show();
    $("#videofile").hide();
    $("#loadingimage").show();
    $("body").css("overflow-y", "hidden");

    var playindex = videoindex;
    $.ajax({
        url: apiurl,
        type: 'GET',
        success(response) {
            let type = response.type;
            if (type != 'livestream') {
                let title = response.title;
                let author = response.author;
                let authorId = response.authorId;
                let authorThumbnail = response.authorThumbnails['2'].url;
                let authorThumbnailString = encodeURIComponent(authorThumbnail)
                let published = response.publishedText;
                var stream = response.formatStreams[0];
                var descriptionHtml = response.descriptionHtml;
                var audiostream = '';
                if (streamquality != '360p') {
                    console.log('Looking for adaptive format..')
                    $.each(response.adaptiveFormats, function (i, format) {
                        if (format.resolution == streamquality && format.encoding == "h264") {
                            stream = format;
                            console.log(format);
                            return false; // stops the loop
                        }
                    });
                }
                $.each(response.adaptiveFormats, function (i, format) {
                    if (format.container == 'm4a' && audiostream == '') {
                        audiostream = format;
                        return false;
                    }
                });

                // channelimage
                let channelimage = `<img src="` + authorThumbnail + `" onclick='showChannel("` + authorId + `")'/>`

                // overlay title
                let infotext = title + `<br/>` + author;
                

                // overlay extra buttons for sharing and liking
                let sharelink = 'https://youtu.be/' + id
                var likeimage = '';
                if (isSubscribed(authorId)) {
                    likeimage = 'images/heart-filled-red.png';
                }
                else {
                    likeimage = 'images/heart-empty.png';
                }
                let extrabuttons = `<table style="width:100%; height:100%;"><tr>`;
                extrabuttons += `<td><div id="likebutton" class="likebutton" onclick='toggleChannel("` + authorId + `","` + author + `","` + authorThumbnail + `", "videoplayer")'><img src="` + likeimage + `"></div></td></tr>`;
                let videourl = stream.url;
                var downloadurl = response.formatStreams[0].url;
                let videoname = (title + '.mp4').replace(/['/\\?#%*:|"<>]+/g, '-')
                let name = title.replace(/['/\\?#%*:|"<>]+/g, '-')

                var image = '';
                $.each(response.videoThumbnails, function (i, thumbnail) {
                    if (thumbnail.quality == "medium") {
                        image = thumbnail.url;
                        return false; // stops the loop
                    }
                });

                extrabuttons += `<tr><td style="width:20%"><div id="sharebutton" onclick='copyToClipboard("` + sharelink + `")'><img src="images/link.png"></div></td></tr>
                             <tr><td><div id="downloadbutton" onclick='downloadVideo("` + downloadurl + `","` + videoname + `","` + name + `","` + image + `","` + author + `","` + authorId + `","` + authorThumbnail + `")'><img src="images/download.png"></div></td></tr>`;
                extrabuttons += `</table>`;

                if (videoActive && playindex == videoindex) {
                    $('#videofile').attr('src', stream.url);
                    $('#audiofile').attr('src', audiostream.url);
                    $("#loadingimage").hide();

                    $("#channelimage").html(channelimage);
                    $("#videotitle").html(infotext);
                    $("#extrabuttons").html(extrabuttons);
                    $("#videodescription").html("<h3>" + infotext + ", " + published + "</h3>" + descriptionHtml);
                    videoResize()
                    $("#videofile").show();
                    
                    addHistoryItem(id, title, image, authorId, author);
                }
                
            }
            else {
                if (videoActive) {
                    $("#videotitle").css("margin-top", "40%")
                    $("#videotitle").html('Livestreams are not supported yet.')
                    $("#loadingimage").hide();
                }
            }
        },
        error(jqXHR, status, errorThrown) {
            if (trycount < 11) {
                if (videoActive) {
                    console.log('failed to fetch ' + apiurl + '. Will retry in 2s ..')
                    $("#videotitle").html('failed to fetch <br/>' + apiurl + '. Will retry in 2s ..')
                    trycount++;
                    setTimeout(playVideo(id, trycount), 2000)
                }
            }
            else {
                if (videoActive) {
                    console.log('failed to fetch ' + apiurl + '. Tried it for 10 times.')
                    $("#loadingimage").hide();
                    $("#errortext").html('Could not fetch video. Please check your connection or try another server from the settings');
                    $("#errortext").show();
                }
            }
        },
    });
}

function playDownload(fileName, title, author, authorId, authorThumbnail) {
    console.log(authorThumbnail);
    videoActive = true;
    videoLocation = 'local';

    let downloadPath = downloadFolder.replace(/\\/g, "/");
    let videosource = downloadFolder + '\\' + fileName;
    //let videosource = 'file:///' + downloadPath + '/' + fileName;
    
    $("#videodescription").hide();
    $("#videodescription").html('');
    $("#errortext").hide();
    $("#videoplayer").show();
    $("#closevideo").show();
    $("#videofile").hide();
    $("body").css("overflow-y", "hidden");    

    // channelimage
    let channelimage = `<img src="` + authorThumbnail + `" onclick='showChannel("` + authorId + `")'/>`

    // overlay title
    let infotext = title + `<br/>` + author;

    // overlay extra buttons for sharing and liking
    let sharelink = ''
    var likeimage = '';

    let extrabuttons = `<div id="likebutton" class="likebutton"><img src="images/play.png"></div>
                        <div id="sharebutton"><img src="images/play.png"></div>
                        <div id="downloadbutton")'><img src="images/play.png"></div>`;
    if (videoActive) {
        try {
            Windows.Storage.StorageFile.getFileFromPathAsync(videosource).then(function (file) {
                loadLocalVideo(file);
            })
        }
        catch (error) {
            console.log("Error accessing file: " + error.message);
        };
        
        console.log(videosource);
        $("#loadingimage").hide();
        $("#channelimage").html(channelimage);
        $("#videotitle").html(infotext);
        $("#extrabuttons").html(extrabuttons);
        videoResize()
        $("#videofile").show();
    }

}

function loadLocalVideo(file) {
    var videoElement = document.getElementById("videofile");
    file.openAsync(Windows.Storage.FileAccessMode.read).then(function (stream) {
        videoElement.src = URL.createObjectURL(stream, { oneTimeOnly: true });
    });
}

function videoResize() {
    var orientation;
    var videotype;

    let videoheight = $("#videofile").height()
    let videowidth = $("#videofile").width()
    let bwidth = $(window).width();
    let bheight = $(window).height();
    var setvideowidth;
    var setvideoheight;

    var playtop = 0;
    var playsize = 0;
    var seektop = 0;
    var seeksize = 0;

    var descriptiontop = 0;

    if (bwidth > bheight) { orientation = 'landscape'; }
    else { orientation = 'portrait'; }

    if (videoheight > videowidth) { videotype = 'portrait'; }
    else if (videoheight == videowidth) { videotype = 'square'; }
    else if (videoheight < videowidth) { videotype = 'landscape'; }

    //console.log('orientation: ' + orientation + ', videotype: ' + videotype)

    if (orientation == 'portrait') {
        switch (videotype) {
            case "portrait":
                var setvideowidth = "";
                var setvideoheight = bheight;
                break;
            case "square":
                var setvideowidth = bwidth;
                var setvideoheight = "";
                break;
            case "landscape":
                var setvideowidth = bwidth;
                var setvideoheight = "";
                break;
        }
    }
    else if (orientation == 'landscape') {
        switch (videotype) {
            case "portrait":
                var setvideowidth = "";
                var setvideoheight = bheight;
                break;
            case "square":
                var setvideowidth = "";
                var setvideoheight = bheight;
                break;
            case "landscape":
                var setvideowidth = "";
                var setvideoheight = bheight;
                break;
        }
    }

    if (setvideowidth > bwidth) {
        setvideowidth = bwidth;
    }

    if (setvideoheight > bheight) {
        setvideoheight = bheight;
    }

    $("#videofile").height(setvideoheight);
    $("#videofile").width(setvideowidth);

    videoheight = $("#videofile").height()
    videowidth = $("#videofile").width()

    switch (videotype) {
        case "portrait":
            playsize = videowidth / 4;
            seeksize = videowidth * 0.15;
            extrasize = videowidth * 0.05;
            playtop = (videoheight / 2) - (playsize / 2);
            seektop = (videoheight / 2) - (seeksize / 2);
            extratop = (videoheight * 0.25);
            descriptiontop = (videoheight * 1.2);
            break;
        case "square":
            playsize = videoheight / 4;
            seeksize = videoheight * 0.15;
            extrasize = videoheight * 0.05;
            playtop = (videoheight / 2) - (playsize / 2);
            seektop = (videoheight / 2) - (seeksize / 2);
            extratop = (videoheight * 0.25);
            descriptiontop = (videoheight * 1.2);
            break;
        case "landscape":
            playsize = videoheight / 3;
            seeksize = videoheight * 0.2;
            extrasize = videoheight * 0.05;
            playtop = (videoheight / 2) - (playsize / 2);
            seektop = (videoheight / 2) - (seeksize / 2);
            extratop = (videoheight * 0.25);
            descriptiontop = (videoheight * 1.2);
            break;
    }

    //let topmargin = videoheight + 30;
    let sixteenbynine = (videowidth / 16) * 9;
    if (sixteenbynine > videoheight) {
        topmargin = (videowidth / 16) * 9 + 30;
    }

    //$("#videotitle").css("margin-top", topmargin + "px")

    $("#rewind").css("top", seektop + "px")
    $("#rewind").css("height", seeksize + "px")
    $("#rewind").css("width", seeksize + "px")
    $("#rewind").css("margin-left", (seeksize / 2 * -1) + "px")

    $("#play").css("top", playtop + "px")
    $("#play").css("height", playsize + "px")
    $("#play").css("width", playsize + "px")
    $("#play").css("margin-left", (playsize / 2 * -1) + "px")

    $("#pause").css("top", playtop + "px")
    $("#pause").css("height", playsize + "px")
    $("#pause").css("width", playsize + "px")
    $("#pause").css("margin-left", (playsize / 2 * -1) + "px")

    $("#forward").css("top", seektop + "px")
    $("#forward").css("height", seeksize + "px")
    $("#forward").css("width", seeksize + "px")
    $("#forward").css("margin-right", (seeksize / 2 * -1) + "px")

    if (videoLocation == 'local') {
        $("#extrabuttons").hide();
    }
    else {
        $("#extrabuttons").css("height", (videoheight / 2) + "px")
        $("#extrabuttons").css("width", extrasize + "px")
        $("#extrabuttons").css("top", extratop + "px")
        $("#likebutton").css("width", extrasize + "px")
        $("#sharebutton").css("width", extrasize + "px")
        $("#downloadbutton").css("width", extrasize + "px")
        $("#likebutton").css("height", extrasize + "px")
        $("#sharebutton").css("height", extrasize + "px")
        $("#downloadbutton").css("height", extrasize + "px")
    }

    $("#channelimage").css("height", (extrasize * 3) + "px")
    $("#channelimage").css("width", (extrasize * 3) + "px")

    $("#videotitle").css("left", (extrasize * 3) + "px")
    $("#videotitle").css("height", (extrasize * 3) + "px")
    $("#videotitle").css("right", (extrasize * 3) + "px")
    $("#videotitle").css("font-size", (extrasize / 1.5 ) + "px")

    $("#closevideo").css("height", (extrasize * 3) + "px")
    $("#closevideo").css("width", (extrasize * 3) + "px")
    $("#closevideo").css("line-height", (extrasize * 3) + "px")
    $("#closevideo").css("font-size", (extrasize) + "px")

    $("#seekarea").css("height", (extrasize * 2) + "px");
    $("#seekarea").css("top", (videoheight - (extrasize * 2)) + "px")
    $("#seektext").css("line-height", (extrasize * 2) + "px")
    $("#seektext").css("font-size", (extrasize) + "px")

    if (videotype != "portrait" && orientation == "portrait") {
        $("#videodescription").css("top", descriptiontop + "px");
        $("#videodescription").show();
    }
    else {
        $("#videodescription").hide();
    }
}

function rewindVideo() {
    showControls()
    var video = document.getElementById("videofile");
    video.currentTime -= 15;
}

function toggleVideo() {
    showControls()
    let video = document.getElementById("videofile");
    let audio = document.getElementById("audiofile");

    if (video.paused) {
        video.play();
        if (streamquality != '360p') {
            audio.play();
        }
        $("#pause").show();
        $("#play").hide();
    }
    else {
        video.pause();
        audio.pause();
        $("#play").show();
        $("#pause").hide();
    }
}

function forwardVideo() {
    showControls()
    var video = document.getElementById("videofile");
    video.currentTime += 15;
}

function closeVideoplayer() {
    $("body").css("overflow-y", "visible");
    hideControls();
    $("#videoplayer").hide();
    $('#videofile').attr('src', '');
    $('#audiofile').attr('src', '');
    $("#videofile").height("");
    $("#videofile").width("100%");
    $("#videotitle").html('')
    videoActive = false;
}

function formatDuration(seconds) {
    seconds = Math.round(seconds);
    let hours = 0;
    let minutes = 0;
    var output;

    if (seconds > 3600) {
        hours = Math.floor(seconds / 3600);
        seconds = seconds - (hours * 3600);
    }
    if (seconds > 60) {
        minutes = Math.floor(seconds / 60);
        seconds = seconds - (minutes * 60);
    }

    if (hours < 10) {
        hours = "0" + hours;
    }
    if (minutes < 10) {
        minutes = "0" + minutes;
    }
    if (seconds < 10) {
        seconds = "0" + seconds;
    }
 
    if (hours == "00") {
        output = minutes + ":" + seconds;
    } else {
        output = hours + ":" + minutes + ":" + seconds;
    }
    return output;
}

function updateProgressBar() {
    let video = document.getElementById("videofile");
    var percentage = (100 / video.duration) * video.currentTime;
    $("#seekprogress").css("width", percentage + "%");
    let timePassed = formatDuration(video.currentTime);
    let timeTotal = formatDuration(video.duration);
    $("#seektext").html(timePassed + " / " + timeTotal)
}

function seekClick(e) {
    let video = document.getElementById("videofile");
    let percent = e.offsetX / this.offsetWidth;
    video.currentTime = percent * video.duration;
}

function seekStart(e) {
    seekX = e.touches[0].clientX;
}

function seekMove(e) {
    showControls()
    seekX = e.touches[0].clientX;
    let divWidth = this.offsetWidth;
    if (seekX > divWidth) {
        seekX = divWidth;
    }
    let percent = 100 * seekX / divWidth;
    $("#seekprogress").css("width", percent + "%")
}

function seekEnd(e) {
    let video = document.getElementById("videofile");
    let divWidth = this.offsetWidth;
    if (seekX > divWidth) {
        seekX = divWidth;
    }
    let multiplier = seekX / divWidth;
    video.currentTime = multiplier * video.duration;
}

function showControls() {
    videoResize()

    if (videoActive) {
        let video = document.getElementById("videofile");

        $(".videocontrols").show();
        if (videoLocation == 'local') {
            $("#extrabuttons").hide();
        }
        if (timeoutid) {
            clearTimeout(timeoutid);
        }
        if (video.paused) {
            $("#play").show();
            $("#pause").hide();
        }
        else {
            $("#pause").show();
            $("#play").hide();
            timeoutid = setTimeout(hideControls, 4000);
        }   
    }
    else {
        closeVideoplayer();
    }
}

function hideControls() {
    $(".videocontrols").hide();
}

function syncAudio() {
    if (videoActive && streamquality != '360p') {
        try {
            let video = document.getElementById("videofile");
            let audio = document.getElementById("audiofile");
            audio.currentTime = video.currentTime

            if (video.paused) {
                audio.pause()
            }
            else {
                audio.play()
            }
        }
        catch (e) {
            console.log('Could not sync audio!')
        }
    }
}

function showChannel(id) {
    closeVideoplayer();
    $("#channelviewer").show();
    $("#channelcontent").html('<img src="images/loading.gif" />')
    let requesturl = server + '/api/v1/channels/'+id+'?hl=en-US'

    $.ajax({
        url: requesturl,
        type: 'GET',
        dataType: 'json',
        success(response) {
            let author = response.author;
            let authorThumbnail = response.authorThumbnails[3].url;
            let latest = response.latestVideos;
            let availabletabs = response.tabs;
            
            var channelheader = `<table style="width:100%"><tr>
                                    <td style="width:10%">`;

                                    if (isSubscribed(id)) {
                                        likeimage = 'images/heart-filled-red.png';
                                    }
                                    else {
                                        likeimage = 'images/heart-empty.png';
                                    }
            channelheader += `<div id="likebutton" class="likebutton" onclick='toggleChannel("` + id + `","` + author + `","` + authorThumbnail + `", "channelviewer")'><img src="` + likeimage + `"></div>`;
            channelheader += `</td>
                              <td style="width:20%"><img src="` + authorThumbnail + `" /></td>
                              <td style="font-size:1.5em;font-weight:bold">`+ author + `</td>
                              </tr></table>`;

            var tablist = `<div id="channelvideos" class="channeltab" style="background-color: Highlight" onclick='showChannel("` + id +`")'><img src="images/title_videos.png" style="height:100%;" /></div>`;
            for (var t = 0; t < availabletabs.length; t++) {
                let tabname = availabletabs[t];
                switch (tabname) {
                    /*case "videos":
                        html += '<div class="channeltab">videos</div>';
                        break;*/
                    case "streams":
                        tablist += `<div id="channelstreams" class="channeltab" onclick='showChannelStreams("` + id +`")'><img src="images/title_streams.png" style="height:100%;" /></div>`;
                        break;
                }
            }
            $("#channelheader").html(channelheader)
            $("#channeltablist").html(tablist)

            var html = '';
            for (var i = 0; i < latest.length; i++) {
                var element = latest[i];
                let title = element.title;
                let published = element.publishedText;
                var image = '';
                $.each(element.videoThumbnails, function (i, thumbnail) {
                    if (thumbnail.quality == "medium") {
                        image = thumbnail.url;
                        return false; // stops the loop
                    }
                });
                let videoId = element.videoId;
                html += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + '"/><div class="videoinfo">' + shortStr(title) + '<br/>' + published + '</div></div>';
            }
            $("#channelcontent").html(html)
            applySizing();
        },
        error(jqXHR, status, errorThrown) {
            console.log('failed to fetch ' + requesturl)
        },
    });
}

function showChannelStreams(id) {
    let requesturl = server + '/api/v1/channels/' + id + '/streams?hl=en-US'
    $(".channeltab").css("background-color", "#333");
    $("#channelstreams").css("background-color", "Highlight");
    $("#channelcontent").html('<img src="images/loading.gif" />')

    $.ajax({
        url: requesturl,
        type: 'GET',
        dataType: 'json',
        success(response) {
            var html = '';
            for (var i = 0; i < response.videos.length; i++) {
                var element = response.videos[i];
                let type = element.type;
                let publishedText = element.publishedText;
                if (type == "video" && publishedText != '0 seconds ago') {
                    let title = element.title;
                    var image = '';
                    $.each(element.videoThumbnails, function (i, thumbnail) {
                        if (thumbnail.quality == "medium") {
                            image = thumbnail.url;
                            return false; // stops the loop
                        }
                    });
                    let videoId = element.videoId;
                    html += `<div class="videoitem" onclick='playVideo("` + videoId + `", 1)'><img src="` + image + '"/><div class="videoinfo">' + shortStr(title) + '<br/>' + publishedText + '</div></div>';
                }
                
            }
            $("#channelcontent").html(html)
            applySizing();
        },
        error(jqXHR, status, errorThrown) {
            console.log('failed to fetch ' + requesturl)
        },
    });
}

function closeChannel() {
    $("#channelviewer").hide();
    $("#channelheader").html("");
    $("#channeltablist").html("");
    $("#channelcontent").html('<img src="images/loading.gif" />')
}

function convertImgToBase64(url, callback, outputFormat) {
    var canvas = document.createElement('CANVAS'),
        ctx = canvas.getContext('2d'),
        img = new Image;
    img.crossOrigin = 'Anonymous';
    img.onload = function () {
        var dataURL;
        canvas.height = img.height;
        canvas.width = img.width;
        ctx.drawImage(img, 0, 0);
        dataURL = canvas.toDataURL(outputFormat);
        callback.call(this, dataURL);
        canvas = null;
    };
    img.src = url;
}

function checkFile(fileName, divName) {
    selectFolder().then(function (folder) {
        folder.tryGetItemAsync(fileName).then(function (testFile) {
            if (testFile !== null) {
                console.log(fileName + ': exists');
                $(divName).html(fileName + ': exists')
                return true
            }
            else {
                console.log(fileName + ': file not found');
                $(divName).html(fileName + ': file not found')
                return false
            }
        });
    });
}

function printDownload(fileName, name, image, author, authorId, authorThumbnail, divName) {
    selectFolder().then(function (folder) {
        folder.tryGetItemAsync(fileName).then(function (testFile) {
            if (testFile !== null) {
                var html = `<div class="videoitem" style="width: 100%; font-size: 16px;">
                            <img src="`+ image + `" onclick='playDownload("` + fileName + `", "` + name + `", "` + author + `", "` + authorId + `", "` + authorThumbnail +`")' />
                            <div class="videoinfo" style="height: 3em;"><table style="width:100%;height:100%;"><tr><td style="width:90%" onclick='playDownload("` + fileName + `", "` + name + `", "` + author + `", "` + authorId + `", "` + authorThumbnail +`")'>` + microStr(name) + `<br/>` + author + `</td><td style="width:10%;background-color:Highlight" onclick='removeDownload("`+fileName+`")'>X</td></tr></table></div>
                            </div>`;
                $(divName).html(html)
                applySizing();
            }
            else {
                removeDownload(fileName);
                $(divName).html("")
            }
        });
    });
}

// prevents that each back button press will suspend the app
function onBackPressed(event) {
    var navfeed_color = $("#nav_feed").css("background-color");

    if ($('#videoplayer:visible').length > 0) {
        closeVideoplayer();
        event.handled = true;
    }
    else if ($('#channelviewer:visible').length > 0) {
        closeChannel();
        event.handled = true;
    }
    else if (navfeed_color == 'rgb(17, 17, 17)') {
        showFeed()
        event.handled = true;
    }
}

function RememberDownloadFolder(folder) {
    Windows.Storage.AccessCache.StorageApplicationPermissions.futureAccessList.addOrReplace(downloadsFolderToken, folder);
}
function GetDownloadFolder() {
    if (!Windows.Storage.AccessCache.StorageApplicationPermissions.futureAccessList.containsItem(downloadsFolderToken)) return null;
    return Windows.Storage.AccessCache.StorageApplicationPermissions.futureAccessList.getFolderAsync(downloadsFolderToken);
}

function selectNewFolder() {
    var picker = new Windows.Storage.Pickers.FolderPicker();
    picker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.documentsLibrary;

    // Add the file types that should be visible in the picker
    picker.fileTypeFilter.replaceAll(["."]);

    return picker.pickSingleFolderAsync();
}

function selectDownloadPath() {
    try {
        selectNewFolder().then(function (folder) {
            if (folder) {
                selectedFolder = folder;
                downloadFolder = selectedFolder.path;
                localStorage.downloadfolder = downloadFolder;
                RememberDownloadFolder(folder);
                showSettings();
            } else {
                $("#settingstext").html("No folder selected.")
            }
        })
    } catch (error) {
        $("#settingsbutton").html("An error occurred:", error)
        $("#downloadbutton").html('<img src="images/download-red.png">');
    }
};

function selectFolder() {
    var currentFolder = GetDownloadFolder();
    if (currentFolder != null) {
        return currentFolder;
    }
    var picker = new Windows.Storage.Pickers.FolderPicker();
    picker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.documentsLibrary;

    // Add the file types that should be visible in the picker
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

function fileExists(folder, fileName) {
    return folder.tryGetItemAsync(fileName).then(function (testFile) {
        return testFile !== null;
    });
}

function saveFileToFolder(folder, fileUrl, fileName) {
    return fileExists(folder, fileName).then(function (exists) {
        if (!exists) {
            return downloadFile(fileUrl).then(function (fileData) {
                return fileData;
            }).then(function (fileData) {
                return folder.createFileAsync(fileName, Windows.Storage.CreationCollisionOption.generateUniqueName).then(function (file) {
                    return Windows.Storage.FileIO.writeBufferAsync(file, fileData);
                });
            });
        }
    });
}

function downloadVideo(fileUrl, fileName, name, image, author, authorId, authorThumbnail) {
    fileName = fileName.replace(/ /g, "_");
    addDownloadhistoryItem(fileName, name, image, author, authorId, authorThumbnail)
    try {
        selectFolder().then(function (folder) {
            if (folder) {
                selectedFolder = folder;
                downloadFolder = selectedFolder.path;
                localStorage.downloadfolder = downloadFolder;
                RememberDownloadFolder(folder);
                console.log("Downloading video to " + selectedFolder.path + " ..")
                $("#sharetext").html("Downloading video to " + selectedFolder.path + " ..")
                $("#downloadbutton").html('<img src="images/download-running.gif">');
                return saveFileToFolder(folder, fileUrl, fileName);
            } else {
                console.log("No folder selected.");
                $("#sharetext").html("No folder selected.")
                $("#downloadbutton").html('<img src="images/download.png">');
            }
        }).then(function () {
            console.log("Downloaded to " + selectedFolder.path)
            $("#sharetext").html("Downloaded to " + selectedFolder.path)
            $("#downloadbutton").html('<img src="images/download-blue.png">');
            setTimeout(clearSharetext, 3000)
        });
    } catch (error) {
        $("#sharetext").html("An error occurred:", error)
        $("#downloadbutton").html('<img src="images/download-red.png">');
    }
};

$(document).ready(function () {
    try {
        Windows.UI.Core.SystemNavigationManager.getForCurrentView().addEventListener("backrequested", onBackPressed);
        appVersion = Windows.ApplicationModel.Package.current.id.version;
        appstring = `${appVersion.major}.${appVersion.minor}.${appVersion.build}`;
        var currentView = Windows.UI.Core.SystemNavigationManager.getForCurrentView();
        currentView.appViewBackButtonVisibility = Windows.UI.Core.AppViewBackButtonVisibility.visible;
    }
    catch(e) {
        console.log('Windows namespace not available, backbutton listener and versioninfo skipped.')
        appstring = 'n/a';
    }

    videofile = $("#videofile")
    videofile.on("click", videoClick);
    videofile.on("error", videoError);
    videofile.on("play", function () { showControls(); syncAudio() });
    videofile.on("pause", function () { showControls(); syncAudio() });
    videofile.on("seeked", function () { showControls(); syncAudio() });
    window.addEventListener("orientationchange", function () {
        videoResize()
    });
    window.addEventListener("resize", function () {
        videoResize()
    });
    videofile.on("touchmove", showControls);
    videofile.on("timeupdate", updateProgressBar);

    let seekArea = $("#seekarea")
    seekArea.on("click", seekClick);
    seekArea.on("touchstart", seekStart);
    seekArea.on("touchmove", seekMove);
    seekArea.on("touchend", seekEnd);

    document.onselectstart = new Function("return false")

    loadSettings();
    getServerlist();
    loadSubscriptions();
    showFeed();
    loadHistory();
    loadSearchHistory();
    loadDownloadHistory();
    setInterval(syncAudio, 30000);
});