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

function initializeMultipleSelection(id) {
  $('#' + id).multiselect({
    buttonContainer: '<div id="' + id + '-container"></div>',
    onChange: function (options, selected) {
      // Get checkbox corresponding to option:
      var value = $(options).val();
      var $input = $('#' + id + '-container input[value="' + value + '"]');

      // Adapt label class:
      if (selected) {
        $input.closest('label').addClass('active');
      } else {
        $input.closest('label').removeClass('active');
      }
    }
  });
}