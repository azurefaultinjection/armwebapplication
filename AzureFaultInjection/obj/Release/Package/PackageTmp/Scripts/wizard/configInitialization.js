$(document).ready(function () {
    /*"westcentalus": "westus",
      "germanycental": "uksouth",
      "germanynortheast": "uksouth",
      "ukwest": "uksouth",
      "westindia": "centralindia",
      "korealcentral": "japanwest",
        "koreasouth": "japanwest"
    */
    var regions = [
        { id: "eastus2", name: "East US 2" },
        { id: "centralus", name: "Central US" },
        { id: "westeurope", name: "West Europe" },
        { id: "francecentral", name: "France Central" }
    ];
    $('#example-getting-started').multiselect();
    $('form').on('submit', onSubmit);
    $('form').on('click',
        function (e) {
            $('multiselect-native-select ul').css('display', 'none');
        });
});

$('#myWizard').easyWizard({
    buttonsClass: 'btn',
    submitButtonClass: 'btn btn-info',
    before: function (wizardObj, currentStepObj, nextStepObj) {
        if (!currentStepObj || currentStepObj[0].getAttribute("id") !== 'authenticate') {
            return;
        }

        getSubscriptions(currentStepObj);
    },
    after: function (wizardObj, prevStepObj, currentStepObj) {
        //alert('Hello, I\'am the after callback');
    },
    beforeSubmit: function (wizardObj) {
        // alert('Hello, I\'am the beforeSubmit callback');
    }
});

function isValid(currentStepObj) {
    if (currentStepObj[0].getAttribute("id") === 'authenticate') {
        var tenantId = currentStepObj.find("#inputTenantId").val();
        var clientId = currentStepObj.find("#inputClientId").val();
        var clientSecret = currentStepObj.find("#inputClientSecret").val();
        if (!tenantId) {
            return;
        }
    }
}

function getResourceGroups(subscription) {
    var tenantId = $.find("#inputTenantId")[0].value;
    var clientId = $.find("#inputClientId")[0].value;
    var clientSecret = $.find("#inputClientSecret")[0].value;
    var request = $.ajax({
        url: "api/FaultInjection/getresourcegroups",
        type: "GET",
        data: { tenantId: tenantId, clientId: clientId, clientSecret: clientSecret, subscription: subscription }
    });
    request.done(function (result) {
        if (!result) {
            console.log("resource group list is empty");
            return;
        }
        console.log("InItlize Multi list");

        bindOptions($('#excludedResourceGroups'), result);
        bindOptions($('#includedResourceGroups'), result);
        initializeMultipleSelection("excludedResourceGroups");
        initializeMultipleSelection("includedResourceGroups");
    });

    request.fail(function (jqXHR, textStatus) {
        alert("Request failed: " + textStatus);
    });
}

function bindOptions($element, result) {
    $element.empty();
    $.each(result, function (index, item) {
        $element.append(
            $('<option/>', {
                value: item.id,
                text: item.displayName ? item.displayName : item.name
            })
        );
    });
}

$("#selectSubscription").change(function () {
    var subscription = this.value;
    getResourceGroups(subscription);
});

function getSubscriptions(currentStepObj) {
    var tenantId = currentStepObj.find("#inputTenantId").val();
    var clientId = currentStepObj.find("#inputClientId").val();
    var clientSecret = currentStepObj.find("#inputClientSecret").val();
    var request = $.ajax({
        url: "api/FaultInjection/getsubscriptions",
        type: "GET",
        data: { tenantId: tenantId, clientId: clientId, clientSecret: clientSecret }
    });
    request.done(function (result) {
        if (!result) {
            console.log("subscription list is empty");
            return;
        }

        bindOptions($('#selectSubscription'), result);
        getResourceGroups(result[0].id);
    });

    request.fail(function (jqXHR, textStatus) {
        alert("Request failed: " + textStatus);
    });
}

var rGrps = {
    excludedResourceGroups: {},
    includedResourceGroups: {}
};

function initializeMultipleSelection(id) {
    $('#' + id).multiselect({
        buttonContainer: '<div id="' + id + '-container"></div>',
        onChange: function (option, checked, select) {
            var val = $(option).val();
            rGrps[id][val] = checked;
            //// Get checkbox corresponding to option:
            //var value = $(options).val();
            //var $input = $('#' + id + '-container input[value="' + value + '"]');

            //// Adapt label class:
            //if (selected) {
            //  $input.closest('label').addClass('active');
            //} else {
            //  $input.closest('label').removeClass('active');
            //}
        }
    });

    $('#' + id + '-container').find('button').on('click', function (e) {
        var display = $(e.target).next('ul').css('display');
        if (display === 'block')
            $(e.target).next('ul').css('display', 'none');
        else
            $(e.target).next('ul').css('display', 'block');
    });
}

function onSubmit(e) {
    e.preventDefault();
    var values = {};
    $.each($(e.target).serializeArray(), function (i, field) {
        if (field.value === 'on') {
            field.value = true;
        }
        if (field.value === 'off') {
            field.value = false;
        }
        values[field.name] = field.value;
    });
    console.log(values, rGrps);
    function getSubsId(id) {
        return id.split('/')[2];
    }

    var dataFormat = {
        //"ClientConfig": {
        /*will allow chaos on multiple subscriptions?*/
        "selectedSubscription": getSubsId(values['microsoft.chaos.client.subscription.id']),
        "tenantId": values['microsoft.chaos.client.tenant.id'],
        "clientId": values['microsoft.chaos.client.id'],
        "clientSecret": values['microsoft.chaos.client.secretKey'],
        "storageAccountName": String(new Date().valueOf()),
        "selectedRegion": values['microsoft.chaos.client.region'],
        "selectedDeploymentRg": ".azureChaosMonkey",
        // },
        //"ChaosConfig": {
        "schedulerFrequency": values['microsoft.chaos.scheduler.frequency'],
        "rollbackFrequency": values['microsoft.chaos.rollback.frequency'],
        "triggerFrequency": values["microsoft.chaos.trigger.frequency"],
        "crawlerFrequency": values["microsoft.chaos.crawler.frequency"],
        "microsoft.chaos.enabled": values["microsoft.chaos.enabled"],
        "microsoft.chaos.leashed": values["microsoft.chaos.leashed"],
        "microsoft.chaos.meantime": values["microsoft.chaos.meantime"],
        "microsoft.chaos.minimumtime": values["microsoft.chaos.minimumtime"],
        "microsoft.chaos.blackListedResourceGroups": Object.keys(rGrps.excludedResourceGroups)
            .filter(grp => rGrps.excludedResourceGroups[grp]),
        "microsoft.chaos.inclusiveOnlyResourceGroups": Object.keys(rGrps.includedResourceGroups)
            .filter(grp => rGrps.includedResourceGroups[grp]),
        "microsoft.chaos.notification.global.enabled": false,
        "microsoft.chaos.notification.sourceEmail": "chaos@domail.com",
        "microsoft.chaos.notification.global.receiverEmail": "chaosgroup@domain.com",
        "microsoft.chaos.AZ": {
            "isAvZonesEnabled": $("input[name='microsoft.chaos.AZ.enabled']").val() === 'on',
            "AvZoneRegions": ["eastus2", "centralus", "westeurope"]
        },
        "microsoft.chaos.VM": {
            "isVmEnabled": $("input[name='microsoft.chaos.VM.enabled']").val() === 'on',
            "vmTerminationPercentage": values["microsoft.chaos.singleInstanceVm.percentageTermination"]
        },
        "microsoft.chaos.SS": {
            "isVmssEnabled": $("input[name='microsoft.chaos.SS.enabled']").val() === 'on',
            "vmssTerminationPercentage": values["microsoft.chaos.SS.percentageTermination"]
        },
        "microsoft.chaos.AS": {
            "isAvSetsEnabled": $("input[name='microsoft.chaos.AS.enabled']").val() === 'on',
            "isAvSetsFaultDomainEnabled": $("#faultDomain").val() === 'on',
            "isAvSetsUpdateDomainEnabled": $("#updateDomain").val() === 'on'
        }
        //}
    }
    console.log(dataFormat);

    var request = $.ajax({
        url: "api/FaultInjection/createblob",
        type: "POST",
        data: dataFormat
    });
    request.done(function (result) {
        if (!result) {
            console.log("subscription list is empty");
            return;
        }
    });

    request.fail(function (jqXHR, textStatus) {
        alert("Request failed: " + textStatus);
    });
}